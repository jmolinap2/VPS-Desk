using System.IO.Compression;
using System.Security.Cryptography;
using VpsDesk.Application.Runtime;

namespace VpsDesk.Desktop.Services;

/// <summary>
/// Creates portable encrypted backups. The format is intentionally self-contained:
/// magic/version, salt, nonce, authentication tag and an AES-GCM encrypted ZIP payload.
/// </summary>
public sealed class EncryptedBackupService
{
    private static readonly byte[] Magic = "VPSBKP01"u8.ToArray();
    private const int SaltLength = 16;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int KeyDerivationIterations = 310_000;

    public async Task CreateAsync(string destinationPath, string password, CancellationToken cancellationToken = default)
    {
        ValidatePassword(password);
        var payload = CreateArchivePayload(cancellationToken);
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var key = DeriveKey(password, salt);
        var cipherText = new byte[payload.Length];
        var tag = new byte[TagLength];

        try
        {
            using var cipher = new AesGcm(key, TagLength);
            cipher.Encrypt(nonce, payload, cipherText, tag, Magic);

            var temporaryPath = destinationPath + ".tmp";
            await using (var output = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await output.WriteAsync(Magic, cancellationToken);
                await output.WriteAsync(salt, cancellationToken);
                await output.WriteAsync(nonce, cancellationToken);
                await output.WriteAsync(tag, cancellationToken);
                await output.WriteAsync(cipherText, cancellationToken);
            }
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public async Task RestoreAsync(string sourcePath, string password, CancellationToken cancellationToken = default)
    {
        ValidatePassword(password);
        var bytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken);
        var minimumLength = Magic.Length + SaltLength + NonceLength + TagLength;
        if (bytes.Length <= minimumLength || !bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new InvalidDataException("El archivo no es una copia cifrada válida de VPS Desk.");
        }

        var salt = bytes.AsSpan(Magic.Length, SaltLength).ToArray();
        var nonce = bytes.AsSpan(Magic.Length + SaltLength, NonceLength).ToArray();
        var tag = bytes.AsSpan(Magic.Length + SaltLength + NonceLength, TagLength).ToArray();
        var cipherText = bytes.AsSpan(minimumLength).ToArray();
        var plainText = new byte[cipherText.Length];
        var key = DeriveKey(password, salt);

        try
        {
            using var cipher = new AesGcm(key, TagLength);
            try
            {
                cipher.Decrypt(nonce, cipherText, tag, plainText, Magic);
            }
            catch (CryptographicException)
            {
                throw new InvalidDataException("La contraseña es incorrecta o la copia está dañada.");
            }

            var stagingDirectory = Path.Combine(Path.GetTempPath(), "VpsDeskRestore", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(stagingDirectory);
                ExtractArchiveSafely(plainText, stagingDirectory);
                RestoreDirectory(Path.Combine(stagingDirectory, "roaming"), VpsDeskDataPaths.RoamingRoot);
                RestoreDirectory(Path.Combine(stagingDirectory, "local"), VpsDeskDataPaths.LocalRoot);
                RestoreDirectory(Path.Combine(stagingDirectory, "secrets"), Path.Combine(AppContext.BaseDirectory, "secrets"));
                RestoreFile(Path.Combine(stagingDirectory, "bootstrap", ".env"), Path.Combine(AppContext.BaseDirectory, ".env"));
            }
            finally
            {
                if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plainText);
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static byte[] CreateArchivePayload(CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddDirectory(archive, VpsDeskDataPaths.RoamingRoot, "roaming", cancellationToken);
            if (!PathsMatch(VpsDeskDataPaths.LocalRoot, VpsDeskDataPaths.RoamingRoot))
            {
                AddDirectory(archive, VpsDeskDataPaths.LocalRoot, "local", cancellationToken);
            }

            AddDirectory(archive, Path.Combine(AppContext.BaseDirectory, "secrets"), "secrets", cancellationToken);
            AddFile(archive, Path.Combine(AppContext.BaseDirectory, ".env"), "bootstrap/.env", cancellationToken);
        }
        return stream.ToArray();
    }

    private static void AddDirectory(ZipArchive archive, string directory, string archiveRoot, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(directory, file).Replace('\\', '/');
            AddFile(archive, file, $"{archiveRoot}/{relativePath}", cancellationToken);
        }
    }

    private static void AddFile(ZipArchive archive, string sourcePath, string entryPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath)) return;
        cancellationToken.ThrowIfCancellationRequested();
        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var output = entry.Open();
        input.CopyTo(output);
    }

    private static void ExtractArchiveSafely(byte[] payload, string destinationRoot)
    {
        using var stream = new MemoryStream(payload, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var root = Path.GetFullPath(destinationRoot) + Path.DirectorySeparatorChar;

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            var destination = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("La copia contiene una ruta no permitida.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input = entry.Open();
            using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
    }

    private static void RestoreDirectory(string sourceDirectory, string destinationDirectory)
    {
        if (!Directory.Exists(sourceDirectory)) return;
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var destination = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static void RestoreFile(string sourcePath, string destinationPath)
    {
        if (!File.Exists(sourcePath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    private static byte[] DeriveKey(string password, byte[] salt)
        => Rfc2898DeriveBytes.Pbkdf2(password, salt, KeyDerivationIterations, HashAlgorithmName.SHA256, 32);

    private static bool PathsMatch(string left, string right)
        => Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar)
            .Equals(Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private static void ValidatePassword(string password)
    {
        if (password.Length < 12)
        {
            throw new ArgumentException("Usa una contraseña de al menos 12 caracteres.");
        }
    }
}
