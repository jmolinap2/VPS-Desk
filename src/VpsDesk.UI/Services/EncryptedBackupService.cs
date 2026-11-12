using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using VpsDesk.Application.Runtime;

namespace VpsDesk.Desktop.Services;

/// <summary>Portable application data archives, optionally protected with AES-GCM.</summary>
public sealed class EncryptedBackupService
{
    private static readonly byte[] Magic = "VPSBKP01"u8.ToArray();
    private const int SaltLength = 16;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private static readonly SemaphoreSlim OperationLock = new(1);
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public async Task<EncryptedBackupCreateResult> CreateAsync(string destinationPath, string password,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var temporaryPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            EncryptedBackupCreateResult result;
            await using (var output = File.Create(temporaryPath))
                result = await CreatePortableAsync(output, true, password, true, progress, cancellationToken);
            File.Move(temporaryPath, destinationPath, overwrite: true);
            return result;
        }
        finally { File.Delete(temporaryPath); }
    }

    public async Task<EncryptedBackupRestoreResult> RestoreAsync(string sourcePath, string password,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        await using var source = File.OpenRead(sourcePath);
        return await RestorePortableAsync(source, password, progress, cancellationToken);
    }

    public static async Task<bool> IsEncryptedAsync(Stream source, CancellationToken cancellationToken = default)
    {
        var header = new byte[Magic.Length];
        var read = await source.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        return read == Magic.Length && header.AsSpan().SequenceEqual(Magic);
    }

    public async Task<EncryptedBackupCreateResult> CreatePortableAsync(Stream destination, bool encrypt, string? password,
        bool includeCredentials = true, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (encrypt) ValidatePassword(password);
        await OperationLock.WaitAsync(cancellationToken);
        byte[]? payload = null;
        byte[]? key = null;
        try
        {
            payload = CreateArchivePayload(includeCredentials, progress, cancellationToken, out var fileCount);
            if (fileCount == 0) throw new InvalidDataException("No se encontraron datos para respaldar.");
            var size = (long)payload.Length;
            if (encrypt)
            {
                progress?.Report("Cifrando el respaldo con AES-GCM…");
                var salt = RandomNumberGenerator.GetBytes(SaltLength);
                var nonce = RandomNumberGenerator.GetBytes(NonceLength);
                var tag = new byte[TagLength];
                key = DeriveKey(password!, salt);
                var cipherText = new byte[payload.Length];
                using var cipher = new AesGcm(key, TagLength);
                cipher.Encrypt(nonce, payload, cipherText, tag, Magic);
                await destination.WriteAsync(Magic, cancellationToken);
                await destination.WriteAsync(salt, cancellationToken);
                await destination.WriteAsync(nonce, cancellationToken);
                await destination.WriteAsync(tag, cancellationToken);
                await destination.WriteAsync(cipherText, cancellationToken);
                size += Magic.Length + SaltLength + NonceLength + TagLength;
            }
            else await destination.WriteAsync(payload, cancellationToken);
            await destination.FlushAsync(cancellationToken);
            progress?.Report($"Respaldo exportado. {fileCount} archivos, {size:N0} bytes.");
            return new EncryptedBackupCreateResult(fileCount, size);
        }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            if (payload is not null) CryptographicOperations.ZeroMemory(payload);
            OperationLock.Release();
        }
    }

    public async Task<EncryptedBackupRestoreResult> RestorePortableAsync(Stream source, string? password,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        await OperationLock.WaitAsync(cancellationToken);
        byte[]? bytes = null;
        byte[]? payload = null;
        byte[]? key = null;
        var staging = Path.Combine(Path.GetTempPath(), "VpsDeskRestore", Guid.NewGuid().ToString("N"));
        try
        {
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, cancellationToken);
            bytes = buffer.ToArray();
            if (bytes.Length < 4) throw new InvalidDataException("El archivo de respaldo está vacío o incompleto.");
            var encrypted = bytes.Length >= Magic.Length && bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic);
            if (encrypted)
            {
                ValidatePassword(password);
                var headerLength = Magic.Length + SaltLength + NonceLength + TagLength;
                if (bytes.Length <= headerLength) throw new InvalidDataException("El respaldo está incompleto.");
                var salt = bytes.AsSpan(Magic.Length, SaltLength).ToArray();
                var nonce = bytes.AsSpan(Magic.Length + SaltLength, NonceLength).ToArray();
                var tag = bytes.AsSpan(Magic.Length + SaltLength + NonceLength, TagLength).ToArray();
                key = DeriveKey(password!, salt);
                payload = new byte[bytes.Length - headerLength];
                using var cipher = new AesGcm(key, TagLength);
                try { cipher.Decrypt(nonce, bytes.AsSpan(headerLength), tag, payload, Magic); }
                catch (CryptographicException) { throw new InvalidDataException("La contraseña es incorrecta o el respaldo está dañado."); }
            }
            else payload = bytes;

            Directory.CreateDirectory(staging);
            ExtractArchiveSafely(payload, staging, cancellationToken);
            var manifestPath = Path.Combine(staging, "vpsdesk-backup.json");
            BackupManifest? manifest = null;
            if (File.Exists(manifestPath))
            {
                manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath));
                if (manifest is not { Format: "VPSDesk.ApplicationData", Version: 2 })
                    throw new InvalidDataException("Versión de respaldo de aplicación incompatible.");
            }
            else if (!encrypted) throw new InvalidDataException("El ZIP carece del manifiesto de VPS Desk.");

            if (manifest is not null) ValidateFileHashes(staging, manifest);
            PreparePortableCredentials(staging, manifest);
            var plan = BuildRestorePlan(staging);
            if (plan.Count == 0) throw new InvalidDataException("El respaldo carece de datos restaurables.");
            cancellationToken.ThrowIfCancellationRequested();
            ApplyRestorePlan(plan, staging, progress);
            progress?.Report("Datos restaurados. Reinicia VPS Desk para cargarlos.");
            return new EncryptedBackupRestoreResult(plan.Count(p => p.Category == "roaming"), plan.Count(p => p.Category == "local"),
                plan.Count(p => p.Category == "secrets"), plan.Any(p => p.Category == "bootstrap"));
        }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            if (payload is not null) CryptographicOperations.ZeroMemory(payload);
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            finally { OperationLock.Release(); }
        }
    }

    private static byte[] CreateArchivePayload(bool includeCredentials, IProgress<string>? progress, CancellationToken token, out int fileCount)
    {
        using var buffer = new MemoryStream();
        fileCount = 0;
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var profiles = new ServerProfileStore().Load().Servers;
        var bootstrapKey = BootstrapServerProfileLoader.Load().Profile?.PrivateKeyPath;
        var keySources = profiles.Select(p => p.PrivateKeyPath).Append(bootstrapKey)
            .Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal).ToArray();
        var excludedPaths = includeCredentials ? new HashSet<string>() : keySources
            .Select(path => Path.GetFullPath(path!, AppContext.BaseDirectory)).ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (!includeCredentials && BootstrapServerProfileLoader.FindEnvFile() is { } environmentPath)
            excludedPaths.Add(Path.GetFullPath(environmentPath));
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            fileCount += AddDirectory(archive, VpsDeskDataPaths.RoamingRoot, "roaming", includeCredentials, hashes, excludedPaths, progress, token);
            if (!PathsMatch(VpsDeskDataPaths.LocalRoot, VpsDeskDataPaths.RoamingRoot))
                fileCount += AddDirectory(archive, VpsDeskDataPaths.LocalRoot, "local", includeCredentials, hashes, excludedPaths, progress, token);

            var keys = new List<BackupKey>();
            if (includeCredentials)
            {
                fileCount += AddDirectory(archive, Path.Combine(AppContext.BaseDirectory, "secrets"), "secrets", true, hashes, excludedPaths, progress, token);
                var envPath = BootstrapServerProfileLoader.FindEnvFile();
                if (envPath is not null) fileCount += AddFile(archive, envPath, "bootstrap/.env", hashes, progress, token);
                foreach (var originalPath in keySources)
                {
                    var fullPath = Path.GetFullPath(originalPath!, AppContext.BaseDirectory);
                    if (!File.Exists(fullPath)) throw new FileNotFoundException("Falta una clave SSH referenciada por la aplicación.", fullPath);
                    var entryPath = "keys/" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(originalPath!))).ToLowerInvariant() + ".key";
                    fileCount += AddFile(archive, fullPath, entryPath, hashes, progress, token);
                    keys.Add(new BackupKey(originalPath!, entryPath));
                }
            }
            var manifest = new BackupManifest("VPSDesk.ApplicationData", 2, DateTimeOffset.UtcNow, includeCredentials, keys,
                includeCredentials ? bootstrapKey : null, hashes);
            using var metadata = archive.CreateEntry("vpsdesk-backup.json").Open();
            JsonSerializer.Serialize(metadata, manifest);
        }
        return buffer.ToArray();
    }

    private static int AddDirectory(ZipArchive archive, string directory, string archiveRoot, bool includeCredentials,
        IDictionary<string, string> hashes, IReadOnlySet<string> excludedPaths, IProgress<string>? progress, CancellationToken token)
    {
        if (!Directory.Exists(directory)) return 0;
        var count = 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var file in Directory.EnumerateFiles(directory, "*", options))
        {
            var relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
            var name = Path.GetFileName(file);
            if (name == "vpsdesk.db" && archiveRoot == "roaming" && !PathsMatch(VpsDeskDataPaths.RoamingRoot, VpsDeskDataPaths.LocalRoot)
                && File.Exists(Path.Combine(VpsDeskDataPaths.LocalRoot, "vpsdesk.db"))) continue;
            if (excludedPaths.Contains(Path.GetFullPath(file)) || IsWithin(file, VpsDeskDataPaths.BackupsRoot) || relative.StartsWith("updates/", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("-wal", StringComparison.Ordinal) || name.EndsWith("-shm", StringComparison.Ordinal)
                || name.EndsWith(".vpsbackup", StringComparison.OrdinalIgnoreCase)
                || (name.StartsWith("VPS-Desk-datos-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || name == "backup-operations.log") continue;
            if (!includeCredentials && (relative.StartsWith("secrets/", StringComparison.OrdinalIgnoreCase)
                || name == ".env" || name.EndsWith(".pem", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".key", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".ppk", StringComparison.OrdinalIgnoreCase))) continue;
            var entryPath = name == "vpsdesk.db" ? "local/vpsdesk.db" : archiveRoot + "/" + relative;
            count += AddFile(archive, file, entryPath, hashes, progress, token);
        }
        return count;
    }

    private static int AddFile(ZipArchive archive, string path, string entryPath, IDictionary<string, string> hashes,
        IProgress<string>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var snapshot = Path.Combine(Path.GetTempPath(), "vpsdesk-db-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var source = path;
            if (Path.GetFileName(path) == "vpsdesk.db") { CopyDataFile(path, snapshot); source = snapshot; }
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var output = archive.CreateEntry(entryPath, CompressionLevel.Optimal).Open();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var chunk = new byte[81920];
            int read;
            while ((read = input.Read(chunk)) > 0)
            {
                token.ThrowIfCancellationRequested();
                output.Write(chunk, 0, read);
                hash.AppendData(chunk, 0, read);
            }
            hashes.Add(entryPath, Convert.ToHexString(hash.GetHashAndReset()));
            progress?.Report($"Incluido {entryPath} ({input.Length:N0} bytes).");
            return 1;
        }
        finally { File.Delete(snapshot); }
    }

    private static void ExtractArchiveSafely(byte[] payload, string staging, CancellationToken token)
    {
        using var source = new MemoryStream(payload, writable: false);
        using var archive = new ZipArchive(source, ZipArchiveMode.Read);
        var allowedRoots = new[] { "roaming", "local", "secrets", "bootstrap", "keys" };
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name)) continue;
            var root = entry.FullName.Split('/')[0];
            if ((entry.FullName != "vpsdesk-backup.json" && !allowedRoots.Contains(root))
                || entry.FullName.Contains('\\') || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("El respaldo contiene una entrada no permitida.");
            var destination = Path.GetFullPath(Path.Combine(staging, entry.FullName));
            if (!IsWithin(destination, staging)) throw new InvalidDataException("El respaldo contiene una ruta no permitida.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input = entry.Open();
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
            input.CopyTo(output);
        }
    }

    private static void PreparePortableCredentials(string staging, BackupManifest? manifest)
    {
        var keyPaths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in manifest?.Keys ?? [])
        {
            var source = Path.GetFullPath(Path.Combine(staging, key.ArchivePath));
            if (!IsWithin(source, Path.Combine(staging, "keys")) || !File.Exists(source))
                throw new InvalidDataException("Falta una clave declarada en el respaldo.");
            var name = Path.GetFileName(key.ArchivePath);
            var stagedKey = Path.Combine(staging, "secrets", name);
            Directory.CreateDirectory(Path.GetDirectoryName(stagedKey)!);
            File.Copy(source, stagedKey);
            keyPaths[key.OriginalPath] = Path.Combine(VpsDeskDataPaths.RoamingRoot, "secrets", name);
        }
        var profilesPath = Path.Combine(staging, "roaming", "servers.json");
        if (File.Exists(profilesPath))
        {
            var profiles = JsonNode.Parse(File.ReadAllText(profilesPath));
            foreach (var profile in profiles?["servers"]?.AsArray() ?? [])
            {
                if (profile?["privateKeyPath"]?.GetValue<string>() is not { } original) continue;
                if (keyPaths.TryGetValue(original, out var restored)) profile["privateKeyPath"] = restored;
                else if (manifest is null)
                {
                    var name = original.Replace('\\', '/').Split('/').Last();
                    if (File.Exists(Path.Combine(staging, "secrets", name)))
                        profile["privateKeyPath"] = Path.Combine(VpsDeskDataPaths.RoamingRoot, "secrets", name);
                }
            }
            File.WriteAllText(profilesPath, profiles!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        var envPath = Path.Combine(staging, "bootstrap", ".env");
        if (File.Exists(envPath) && manifest?.BootstrapKeyPath is { } bootstrapKey && keyPaths.TryGetValue(bootstrapKey, out var restoredKey))
        {
            var lines = File.ReadAllLines(envPath).Select(line => line.Split('=', 2)[0].Trim() == "SSH_KEY_PATH"
                ? "SSH_KEY_PATH=" + restoredKey : line);
            File.WriteAllLines(envPath, lines);
        }
        else if (File.Exists(envPath) && manifest is null)
        {
            var lines = File.ReadAllLines(envPath).Select(line =>
            {
                var parts = line.Split('=', 2);
                if (parts.Length != 2 || parts[0].Trim() != "SSH_KEY_PATH") return line;
                var name = parts[1].Trim().Trim('"').Replace('\\', '/').Split('/').Last();
                return File.Exists(Path.Combine(staging, "secrets", name))
                    ? "SSH_KEY_PATH=" + Path.Combine(VpsDeskDataPaths.RoamingRoot, "secrets", name) : line;
            });
            File.WriteAllLines(envPath, lines);
        }
    }

    private static void ValidateFileHashes(string staging, BackupManifest manifest)
    {
        if (manifest.Hashes is null) throw new InvalidDataException("El respaldo carece de huellas de integridad.");
        var files = Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) != "vpsdesk-backup.json").ToArray();
        if (files.Length != manifest.Hashes.Count) throw new InvalidDataException("El inventario de archivos del respaldo está incompleto.");
        foreach (var file in files)
        {
            var entryPath = Path.GetRelativePath(staging, file).Replace('\\', '/');
            using var source = File.OpenRead(file);
            if (!manifest.Hashes.TryGetValue(entryPath, out var expected)
                || !string.Equals(Convert.ToHexString(SHA256.HashData(source)), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Un archivo del respaldo está dañado. " + entryPath);
        }
    }

    private static List<RestoreItem> BuildRestorePlan(string staging)
    {
        var plan = new List<RestoreItem>();
        foreach (var (category, destinationRoot) in new[]
        {
            ("roaming", VpsDeskDataPaths.RoamingRoot), ("local", VpsDeskDataPaths.LocalRoot),
            ("secrets", Path.Combine(VpsDeskDataPaths.RoamingRoot, "secrets"))
        })
        {
            var sourceRoot = Path.Combine(staging, category);
            if (!Directory.Exists(sourceRoot)) continue;
            foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith("-wal", StringComparison.Ordinal) || file.EndsWith("-shm", StringComparison.Ordinal)
                    || file.EndsWith("-journal", StringComparison.Ordinal)) continue;
                var effectiveRoot = Path.GetFileName(file) == "vpsdesk.db" ? VpsDeskDataPaths.LocalRoot : destinationRoot;
                var destination = Path.GetFullPath(Path.Combine(effectiveRoot, Path.GetRelativePath(sourceRoot, file)));
                if (!IsWithin(destination, effectiveRoot)) throw new InvalidDataException("Destino de restauración no permitido.");
                var duplicate = plan.FirstOrDefault(item => PathsMatch(item.Destination, destination));
                if (duplicate is not null)
                {
                    using var first = File.OpenRead(duplicate.Source);
                    using var second = File.OpenRead(file);
                    if (!SHA256.HashData(first).AsSpan().SequenceEqual(SHA256.HashData(second)))
                        throw new InvalidDataException("El respaldo contiene archivos diferentes para el mismo destino.");
                    continue;
                }
                plan.Add(new RestoreItem(file, destination, Path.GetFileName(file) == "vpsdesk.db" ? "local" : category));
            }
        }
        var envSource = Path.Combine(staging, "bootstrap", ".env");
        if (File.Exists(envSource))
        {
            var envDestination = Environment.GetEnvironmentVariable("VPSDESK_ENV_PATH")
                ?? BootstrapServerProfileLoader.FindEnvFile() ?? Path.Combine(VpsDeskDataPaths.RoamingRoot, ".env");
            plan.RemoveAll(item => PathsMatch(item.Destination, envDestination));
            plan.Add(new RestoreItem(envSource, envDestination, "bootstrap"));
        }
        return plan;
    }

    private static void ApplyRestorePlan(List<RestoreItem> plan, string staging, IProgress<string>? progress)
    {
        var originals = new Dictionary<string, string?>();
        var applied = new List<RestoreItem>();
        foreach (var item in plan)
        {
            if (Directory.Exists(item.Destination)) throw new IOException("Una carpeta ocupa el destino de un archivo del respaldo.");
            var trustedRoot = item.Category switch
            {
                "local" => VpsDeskDataPaths.LocalRoot,
                "bootstrap" => Path.GetDirectoryName(item.Destination)!,
                _ => VpsDeskDataPaths.RoamingRoot
            };
            for (var path = item.Destination; !string.IsNullOrEmpty(path) && IsWithin(path, trustedRoot); path = Path.GetDirectoryName(path))
                if ((File.Exists(path) || Directory.Exists(path)) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                    throw new InvalidDataException("El destino contiene un enlace simbólico.");
            var rollback = Path.Combine(staging, "rollback", originals.Count.ToString(), Path.GetFileName(item.Destination));
            if (File.Exists(item.Destination)) CopyDataFile(item.Destination, rollback);
            originals[item.Destination] = File.Exists(item.Destination) ? rollback : null;
        }
        try
        {
            foreach (var item in plan)
            {
                applied.Add(item);
                CopyDataFile(item.Source, item.Destination);
                progress?.Report($"Restaurado {item.Category}/{Path.GetFileName(item.Destination)}.");
            }
        }
        catch (Exception failure)
        {
            var failures = new List<Exception> { failure };
            foreach (var item in applied.AsEnumerable().Reverse())
            {
                try
                {
                    if (originals[item.Destination] is { } original) CopyDataFile(original, item.Destination);
                    else File.Delete(item.Destination);
                }
                catch (Exception rollbackError) { failures.Add(rollbackError); }
            }
            if (failures.Count > 1) throw new AggregateException("La restauración falló y hubo errores al recuperar el estado previo.", failures);
            throw;
        }
    }

    private static void CopyDataFile(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (Path.GetFileName(source) == "vpsdesk.db" || Path.GetFileName(destination) == "vpsdesk.db")
        {
            using var input = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = source, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            using var output = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString());
            input.Open();
            output.Open();
            input.BackupDatabase(output);
            return;
        }
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(source, temporary);
            File.Move(temporary, destination, overwrite: true);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(destination, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        finally { File.Delete(temporary); }
    }

    private static bool IsWithin(string path, string directory)
        => Path.GetFullPath(path).StartsWith(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, PathComparison);

    private static bool PathsMatch(string left, string right) => Path.GetFullPath(left).Equals(Path.GetFullPath(right), PathComparison);
    private static byte[] DeriveKey(string password, byte[] salt) => Rfc2898DeriveBytes.Pbkdf2(password, salt, 310_000, HashAlgorithmName.SHA256, 32);
    private static void ValidatePassword(string? password)
    {
        if (password is null || password.Length < 12) throw new ArgumentException("Usa una contraseña de al menos 12 caracteres.");
    }

    private sealed record BackupKey(string OriginalPath, string ArchivePath);
    private sealed record BackupManifest(string Format, int Version, DateTimeOffset CreatedAt, bool CredentialsIncluded,
        IReadOnlyList<BackupKey> Keys, string? BootstrapKeyPath, IReadOnlyDictionary<string, string>? Hashes);
    private sealed record RestoreItem(string Source, string Destination, string Category);
}

public sealed record EncryptedBackupCreateResult(int FileCount, long SizeBytes);
public sealed record EncryptedBackupRestoreResult(int RoamingFiles, int LocalFiles, int SecretFiles, bool EnvironmentRestored)
{
    public int TotalFiles => RoamingFiles + LocalFiles + SecretFiles + (EnvironmentRestored ? 1 : 0);
}
