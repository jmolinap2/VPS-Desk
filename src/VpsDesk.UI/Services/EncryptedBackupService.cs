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

    public async Task<EncryptedBackupCreateResult> CreateAsync(
        string destinationPath,
        string password,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidatePassword(password);
        progress?.Report($"Origen de datos: {VpsDeskDataPaths.RoamingRoot}");
        progress?.Report($"Preparando archivos desde {VpsDeskDataPaths.RoamingRoot}...");
        var payload = CreateArchivePayload(cancellationToken, progress, out var fileCount);
        if (fileCount == 0) throw new InvalidDataException("No se encontraron archivos para incluir en la copia.");
        progress?.Report($"Archivo ZIP preparado: {fileCount} archivos, {payload.Length:N0} bytes.");
        progress?.Report("Cifrando la copia...");
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
            progress?.Report($"Copia cifrada guardada: {destinationPath} ({new FileInfo(destinationPath).Length:N0} bytes).");
            return new EncryptedBackupCreateResult(fileCount, new FileInfo(destinationPath).Length);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public async Task<EncryptedBackupRestoreResult> RestoreAsync(
        string sourcePath,
        string password,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidatePassword(password);
        progress?.Report($"Origen seleccionado: {sourcePath}");
        progress?.Report("Leyendo archivo cifrado...");
        var bytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken);
        progress?.Report($"Archivo leído: {bytes.Length:N0} bytes.");
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
            progress?.Report("Validando autenticidad y descifrando con AES-GCM...");
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
                progress?.Report($"Directorio temporal: {stagingDirectory}");
                var extracted = ExtractArchiveSafely(plainText, stagingDirectory, progress);
                if (extracted == 0) throw new InvalidDataException("La copia se descifró, pero no contiene archivos restaurables.");
                progress?.Report($"Extracción terminada: {extracted} archivos.");
                progress?.Report($"Destino de datos (roaming): {VpsDeskDataPaths.RoamingRoot}");
                var roamingFiles = RestoreDirectory(Path.Combine(stagingDirectory, "roaming"), VpsDeskDataPaths.RoamingRoot, progress);
                var localFiles = 0;
                if (!PathsMatch(VpsDeskDataPaths.RoamingRoot, VpsDeskDataPaths.LocalRoot))
                {
                    progress?.Report($"Destino de datos (local): {VpsDeskDataPaths.LocalRoot}");
                    localFiles = RestoreDirectory(Path.Combine(stagingDirectory, "local"), VpsDeskDataPaths.LocalRoot, progress);
                }
                else progress?.Report("Modo portable: los datos local y roaming comparten la misma carpeta; se restaura una sola vez.");

                var secretDestination = Path.Combine(AppContext.BaseDirectory, "secrets");
                progress?.Report($"Destino de secretos: {secretDestination}");
                var secretFiles = RestoreDirectory(Path.Combine(stagingDirectory, "secrets"), secretDestination, progress);
                var envDestination = Path.Combine(AppContext.BaseDirectory, ".env");
                progress?.Report($"Destino de .env: {envDestination}");
                var environmentRestored = RestoreFile(Path.Combine(stagingDirectory, "bootstrap", ".env"), envDestination, progress);
                progress?.Report($"Conteo final: datos={roamingFiles + localFiles}, secretos={secretFiles}, .env={(environmentRestored ? 1 : 0)}.");
                progress?.Report("Restauración terminada correctamente.");
                return new EncryptedBackupRestoreResult(roamingFiles, localFiles, secretFiles, environmentRestored);
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

    private static byte[] CreateArchivePayload(CancellationToken cancellationToken, IProgress<string>? progress, out int fileCount)
    {
        fileCount = 0;
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            fileCount += AddDirectory(archive, VpsDeskDataPaths.RoamingRoot, "roaming", cancellationToken, progress);
            if (!PathsMatch(VpsDeskDataPaths.LocalRoot, VpsDeskDataPaths.RoamingRoot))
            {
                fileCount += AddDirectory(archive, VpsDeskDataPaths.LocalRoot, "local", cancellationToken, progress);
            }

            fileCount += AddDirectory(archive, Path.Combine(AppContext.BaseDirectory, "secrets"), "secrets", cancellationToken, progress);
            fileCount += AddFile(archive, Path.Combine(AppContext.BaseDirectory, ".env"), "bootstrap/.env", cancellationToken, progress);
        }
        return stream.ToArray();
    }

    private static int AddDirectory(ZipArchive archive, string directory, string archiveRoot, CancellationToken cancellationToken, IProgress<string>? progress)
    {
        if (!Directory.Exists(directory)) return 0;
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(directory, file).Replace('\\', '/');
            count += AddFile(archive, file, $"{archiveRoot}/{relativePath}", cancellationToken, progress);
        }
        return count;
    }

    private static int AddFile(ZipArchive archive, string sourcePath, string entryPath, CancellationToken cancellationToken, IProgress<string>? progress)
    {
        if (!File.Exists(sourcePath)) return 0;
        cancellationToken.ThrowIfCancellationRequested();
        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var output = entry.Open();
        input.CopyTo(output);
        progress?.Report($"Incluido: {sourcePath} ({input.Length:N0} bytes).");
        return 1;
    }

    private static int ExtractArchiveSafely(byte[] payload, string destinationRoot, IProgress<string>? progress)
    {
        using var stream = new MemoryStream(payload, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var root = Path.GetFullPath(destinationRoot) + Path.DirectorySeparatorChar;

        var count = 0;
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
            progress?.Report($"Extraído: {entry.FullName} ({entry.Length:N0} bytes).");
            count++;
        }
        return count;
    }

    private static int RestoreDirectory(string sourceDirectory, string destinationDirectory, IProgress<string>? progress)
    {
        if (!Directory.Exists(sourceDirectory)) return 0;
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var destination = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
            var sourceInfo = new FileInfo(file);
            var destinationInfo = new FileInfo(destination);
            if (sourceInfo.Length != destinationInfo.Length)
                throw new IOException($"Verificación de tamaño fallida al copiar '{destination}'.");
            progress?.Report($"Restaurado y verificado: {destination} ({destinationInfo.Length:N0} bytes).");
            count++;
        }
        return count;
    }

    private static bool RestoreFile(string sourcePath, string destinationPath, IProgress<string>? progress)
    {
        if (!File.Exists(sourcePath)) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourcePath, destinationPath, overwrite: true);
        if (new FileInfo(sourcePath).Length != new FileInfo(destinationPath).Length)
            throw new IOException($"Verificación de tamaño fallida al copiar '{destinationPath}'.");
        progress?.Report($"Restaurado y verificado: {destinationPath} ({new FileInfo(destinationPath).Length:N0} bytes).");
        return true;
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

public sealed record EncryptedBackupCreateResult(int FileCount, long SizeBytes);

public sealed record EncryptedBackupRestoreResult(
    int RoamingFiles,
    int LocalFiles,
    int SecretFiles,
    bool EnvironmentRestored)
{
    public int TotalFiles => RoamingFiles + LocalFiles + SecretFiles + (EnvironmentRestored ? 1 : 0);
}
