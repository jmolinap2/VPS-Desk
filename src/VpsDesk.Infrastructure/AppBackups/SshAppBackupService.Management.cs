using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using Renci.SshNet;
using VpsDesk.Application.Abstractions;
using VpsDesk.Application.AppBackups;
using VpsDesk.Domain.Servers;
using VpsDesk.Infrastructure.Ssh;

namespace VpsDesk.Infrastructure.AppBackups;

public sealed partial class SshAppBackupService
{
    public Task<IReadOnlyList<AppBackupInfo>> ListRemoteAsync(ServerProfile server, string? secret, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<AppBackupInfo>>(() =>
        {
            using var client = Connect(server, secret);
            var directory = RemoteDirectory(client);
            if (!client.Exists(directory)) return [];
            if (client.GetAttributes(directory).IsSymbolicLink)
                throw new InvalidOperationException("La carpeta de respaldos del VPS debe ser un directorio real.");

            return client.ListDirectory(directory)
                .Where(file => file.IsRegularFile && !file.IsSymbolicLink && file.Name.EndsWith(".vpsbackup.tar", StringComparison.Ordinal))
                .Select(file => new AppBackupInfo(file.Name, file.FullName, file.Length,
                    new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero), StoredOnServer: true))
                .OrderByDescending(file => file.CreatedAt)
                .ToArray();
        }, cancellationToken);

    public async Task<AppBackupInfo> DownloadAsync(ServerProfile server, AppBackupInfo backup, string? secret,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!backup.StoredOnServer) throw new InvalidOperationException("Selecciona una copia del VPS para descargarla.");
        // Read the expected hash before transferring, so a changed source cannot pass validation.
        await Task.Run(() =>
        {
            using var client = Connect(server, secret);
            ValidateRemoteBackup(client, backup);
        }, cancellationToken);
        var hashScript = "set -eu\np=" + Quote(backup.FullPath) + "\n"
            + "hash=$(sha256sum -- \"$p\" | cut -d' ' -f1)\n"
            + "if [ -f \"$p.sha256\" ]; then test \"$hash\" = \"$(cat \"$p.sha256\")\"; fi\n"
            + "printf '%s\\n' \"$hash\"\n";
        var hashResult = await _ssh.ExecuteAsync(new SshCommandRequest(server,
            "bash -c " + Quote(hashScript), RemoteTimeout), secret, cancellationToken);
        var expectedHash = hashResult.StandardOutput.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (!hashResult.Succeeded || expectedHash?.Length != 64 || !expectedHash.All(Uri.IsHexDigit))
            throw new InvalidDataException("No se pudo obtener la huella SHA-256 del respaldo remoto.");

        Directory.CreateDirectory(GetBackupDirectory(server));
        var finalPath = Path.Combine(GetBackupDirectory(server), backup.FileName);
        var partPath = finalPath + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            await DownloadAsync(server, secret, backup.FullPath, partPath, backup.SizeBytes, progress, cancellationToken);
            await ValidateArchiveAsync(partPath, expectedHash, cancellationToken);
            // An existing verified download can be reused without replacing a local backup.
            if (File.Exists(finalPath))
            {
                await ValidateArchiveAsync(finalPath, expectedHash, cancellationToken);
            }
            else
            {
                File.Move(partPath, finalPath);
            }
            await File.WriteAllTextAsync(finalPath + ".sha256", expectedHash, CancellationToken.None);
            var info = new FileInfo(finalPath);
            return new AppBackupInfo(info.Name, info.FullName, info.Length, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
        }
        finally
        {
            TryDelete(partPath);
        }
    }

    public async Task VerifyAsync(ServerProfile server, AppBackupInfo backup, string? secret, CancellationToken cancellationToken = default)
    {
        if (!backup.StoredOnServer)
        {
            ValidateLocalBackup(server, backup);
            var hashPath = backup.FullPath + ".sha256";
            var expectedHash = File.Exists(hashPath) ? (await File.ReadAllTextAsync(hashPath, cancellationToken)).Trim() : null;
            await ValidateArchiveAsync(backup.FullPath, expectedHash, cancellationToken);
            return;
        }

        await Task.Run(() =>
        {
            using var client = Connect(server, secret);
            ValidateRemoteBackup(client, backup);
        }, cancellationToken);
        var path = Quote(backup.FullPath);
        var script = $"set -euo pipefail\np={path}\n"
            + "test ! -L \"$p\"\n"
            + "list=$(tar -tf \"$p\")\n"
            + "printf '%s\\n' \"$list\" | grep -qE '^\\./(project\\.tar\\.gz|postgres-all\\.sql\\.gz|db/.+\\.dump)$'\n"
            + "if printf '%s\\n' \"$list\" | grep -qx './project.tar.gz'; then tar -xOf \"$p\" ./project.tar.gz | gzip -t; fi\n"
            + "tar -xOf \"$p\" ./manifest.txt >/dev/null\n"
            + "for d in $(printf '%s\\n' \"$list\" | grep -E '^\\./db/.+\\.dump$'); do test \"$(tar -xOf \"$p\" \"$d\" | head -c 5)\" = PGDMP; done\n"
            + "if tar -tf \"$p\" | grep -qx './postgres-all.sql.gz'; then tar -xOf \"$p\" ./postgres-all.sql.gz | gzip -t; fi\n"
            + "if tar -tf \"$p\" | grep -qx './system-config.tar.gz'; then tar -xOf \"$p\" ./system-config.tar.gz | gzip -t; fi\n"
            + "if [ -f \"$p.sha256\" ]; then test \"$(sha256sum \"$p\" | cut -d' ' -f1)\" = \"$(cat \"$p.sha256\")\"; fi\n";
        var result = await _ssh.ExecuteAsync(new SshCommandRequest(server,
            "bash -c " + Quote(script), RemoteTimeout), secret, cancellationToken);
        if (!result.Succeeded) throw new InvalidDataException("Falló la verificación del respaldo remoto. " + result.StandardError.Trim());
    }

    public Task DeleteAsync(ServerProfile server, AppBackupInfo backup, string? secret, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (backup.StoredOnServer)
            {
                using var client = Connect(server, secret);
                ValidateRemoteBackup(client, backup);
                client.DeleteFile(backup.FullPath);
                if (client.Exists(backup.FullPath + ".sha256")) client.DeleteFile(backup.FullPath + ".sha256");
                return;
            }
            ValidateLocalBackup(server, backup);
            File.Delete(backup.FullPath);
            TryDelete(backup.FullPath + ".sha256");
        }, cancellationToken);

    private static SftpClient Connect(ServerProfile server, string? secret)
    {
        var client = new SftpClient(SshConnectionFactory.Create(server, secret, TimeSpan.FromSeconds(20)));
        SshConnectionFactory.ApplyHostKeyPolicy(client, server);
        try
        {
            client.Connect();
            client.OperationTimeout = TimeSpan.FromMinutes(2);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static string RemoteDirectory(SftpClient client) => client.WorkingDirectory.TrimEnd('/') + "/vpsdesk-backups";

    private static void ValidateRemoteBackup(SftpClient client, AppBackupInfo backup)
    {
        var directory = RemoteDirectory(client);
        var expectedPath = directory + "/" + backup.FileName;
        if (backup.FileName != Path.GetFileName(backup.FileName)
            || !backup.FileName.EndsWith(".vpsbackup.tar", StringComparison.Ordinal)
            || !string.Equals(expectedPath, backup.FullPath, StringComparison.Ordinal)
            || client.GetAttributes(directory).IsSymbolicLink)
            throw new InvalidOperationException("La copia está fuera de la carpeta de respaldos del VPS.");

        var attributes = client.GetAttributes(expectedPath);
        if (!attributes.IsRegularFile || attributes.IsSymbolicLink)
            throw new InvalidOperationException("El respaldo debe ser un archivo regular.");
    }

    private void ValidateLocalBackup(ServerProfile server, AppBackupInfo backup)
    {
        var expectedPath = Path.GetFullPath(Path.Combine(GetBackupDirectory(server), backup.FileName));
        if (backup.FileName != Path.GetFileName(backup.FileName)
            || !backup.FileName.EndsWith(".vpsbackup.tar", StringComparison.Ordinal)
            || !string.Equals(expectedPath, Path.GetFullPath(backup.FullPath), StringComparison.Ordinal)
            || new FileInfo(expectedPath).LinkTarget is not null)
            throw new InvalidOperationException("La copia está fuera de la carpeta local de respaldos.");
    }

    private static async Task ValidateArchiveAsync(string path, string? expectedHash, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        if (expectedHash is not null)
        {
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("La huella SHA-256 del respaldo no coincide.");
            stream.Position = 0;
        }

        using var reader = new TarReader(stream, leaveOpen: true);
        var hasPayload = false;
        var hasManifest = false;
        while (await reader.GetNextEntryAsync(cancellationToken: cancellationToken) is { } entry)
        {
            if (entry.Name == "./manifest.txt") hasManifest = entry.Length > 0;
            if (entry.Name is "./project.tar.gz" or "./postgres-all.sql.gz") hasPayload |= entry.Length > 0;
            if (DatabaseNameFromEntry(entry.Name) is not null && entry.DataStream is { } dump)
            {
                // Formato personalizado de pg_dump: empieza con la firma "PGDMP".
                var signature = new byte[5];
                if (entry.Length < 5 || await dump.ReadAtLeastAsync(signature, 5, throwOnEndOfStream: false, cancellationToken) < 5
                    || System.Text.Encoding.ASCII.GetString(signature) != "PGDMP")
                    throw new InvalidDataException($"El volcado {entry.Name} no tiene el formato de pg_dump.");
                hasPayload = true;
            }
            if (entry.Name.EndsWith(".gz", StringComparison.Ordinal) && entry.DataStream is { } data)
            {
                using var gzip = new GZipStream(data, CompressionMode.Decompress, leaveOpen: true);
                await gzip.CopyToAsync(Stream.Null, cancellationToken);
            }
        }
        if (!hasPayload || !hasManifest) throw new InvalidDataException("El archivo carece de contenido (proyecto o base de datos) o del manifiesto del respaldo.");
    }

    /// <summary>Nombre de la base para una entrada «./db/&lt;base&gt;.dump»; null para cualquier otra.</summary>
    private static string? DatabaseNameFromEntry(string entryName)
    {
        const string prefix = "./db/";
        const string extension = ".dump";
        if (!entryName.StartsWith(prefix, StringComparison.Ordinal) || !entryName.EndsWith(extension, StringComparison.Ordinal)) return null;
        var name = entryName[prefix.Length..^extension.Length];
        return IsSafeDatabaseName(name) ? name : null;
    }
}
