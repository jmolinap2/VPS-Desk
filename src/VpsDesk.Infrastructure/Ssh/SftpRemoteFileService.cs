using System.Text;
using Renci.SshNet;
using VpsDesk.Application.Abstractions;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Infrastructure.Ssh;

public sealed class SftpRemoteFileService : IRemoteFileService
{
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
            client.Connect();
            cancellationToken.ThrowIfCancellationRequested();

            if (!client.Exists(remotePath))
            {
                client.Disconnect();
                return null;
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

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var client = new SftpClient(SshConnectionFactory.Create(server, secret, TimeSpan.FromSeconds(20)));
            client.Connect();
            cancellationToken.ThrowIfCancellationRequested();

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            client.UploadFile(stream, remotePath, true);
            client.Disconnect();
        }, cancellationToken);
    }
}
