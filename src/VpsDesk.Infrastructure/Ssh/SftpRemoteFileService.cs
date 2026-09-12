using System.Text;
using Renci.SshNet;
using VpsDesk.Application.Abstractions;
using VpsDesk.Domain.Files;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Infrastructure.Ssh;

public sealed class SftpRemoteFileService : IRemoteFileService
{
    private const long MaxEditableTextBytes = 2 * 1024 * 1024;

    public async Task<IReadOnlyList<RemoteFileEntry>> ListAsync(
        ServerProfile server,
        string remotePath,
        string? secret,
        CancellationToken cancellationToken = default)
    {
        remotePath = NormalizePath(remotePath);

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var client = new SftpClient(SshConnectionFactory.Create(server, secret, TimeSpan.FromSeconds(15)));
            SshConnectionFactory.ApplyHostKeyPolicy(client, server);
            client.Connect();
            cancellationToken.ThrowIfCancellationRequested();

            var entries = client.ListDirectory(remotePath)
                .Where(entry => entry.Name is not "." and not "..")
                .Select(entry => new RemoteFileEntry(
                    entry.Name,
                    entry.FullName,
                    entry.IsDirectory,
                    entry.Length,
                    new DateTimeOffset(entry.LastWriteTimeUtc, TimeSpan.Zero)))
                .OrderByDescending(entry => entry.IsDirectory)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            client.Disconnect();
            return (IReadOnlyList<RemoteFileEntry>)entries;
        }, cancellationToken);
    }

    public async Task<string?> ReadTextAsync(
        ServerProfile server,
        string remotePath,
        string? secret,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
        {
            throw new ArgumentException("Remote path is required.", nameof(remotePath));
        }

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var client = new SftpClient(SshConnectionFactory.Create(server, secret, TimeSpan.FromSeconds(15)));
            SshConnectionFactory.ApplyHostKeyPolicy(client, server);
            client.Connect();
            cancellationToken.ThrowIfCancellationRequested();

            if (!client.Exists(remotePath))
            {
                client.Disconnect();
                return null;
            }

            var attributes = client.GetAttributes(remotePath);
            if (attributes.IsDirectory)
            {
                throw new InvalidOperationException("The selected path is a directory, not a text file.");
            }

            if (attributes.Size > MaxEditableTextBytes)
            {
                throw new InvalidOperationException("Text editor is limited to files of 2 MB or less in this version.");
            }

            using var stream = new MemoryStream();
            client.DownloadFile(remotePath, stream);
            client.Disconnect();
            return Encoding.UTF8.GetString(stream.ToArray());
        }, cancellationToken);
    }

    public async Task WriteTextAsync(
        ServerProfile server,
        string remotePath,
        string content,
        string? secret,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
        {
            throw new ArgumentException("Remote path is required.", nameof(remotePath));
        }

        var bytes = Encoding.UTF8.GetBytes(content);
        if (bytes.LongLength > MaxEditableTextBytes)
        {
            throw new InvalidOperationException("Text editor is limited to files of 2 MB or less in this version.");
        }

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var client = new SftpClient(SshConnectionFactory.Create(server, secret, TimeSpan.FromSeconds(20)));
            SshConnectionFactory.ApplyHostKeyPolicy(client, server);
            client.Connect();
            cancellationToken.ThrowIfCancellationRequested();

            using var stream = new MemoryStream(bytes, writable: false);
            client.UploadFile(stream, remotePath, true);
            client.Disconnect();
        }, cancellationToken);
    }

    private static string NormalizePath(string remotePath)
    {
        if (string.IsNullOrWhiteSpace(remotePath)) return "/";
        remotePath = remotePath.Trim().Replace('\\', '/');
        if (!remotePath.StartsWith('/')) remotePath = "/" + remotePath;
        while (remotePath.Contains("//", StringComparison.Ordinal))
        {
            remotePath = remotePath.Replace("//", "/", StringComparison.Ordinal);
        }

        return remotePath.Length > 1 ? remotePath.TrimEnd('/') : remotePath;
    }
}
