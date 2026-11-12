using VpsDesk.Domain.Servers;

namespace VpsDesk.Application.AppBackups;

/// <summary>
/// Copias de la aplicación independientes del proveedor: volcado de PostgreSQL,
/// el proyecto (compose, .env, configuración) y la configuración del sistema,
/// descargadas a esta máquina. A diferencia del snapshot de Hostinger (uno solo,
/// caduca al día, no se descarga), aquí se conservan las últimas N copias.
/// </summary>
public interface IAppBackupService
{
    /// <summary>Carpeta local donde se guardan las copias de este servidor.</summary>
    string GetBackupDirectory(ServerProfile server);

    /// <summary>Copias locales de este servidor, de la más reciente a la más antigua.</summary>
    IReadOnlyList<AppBackupInfo> List(ServerProfile server);

    /// <summary>Enumera las copias guardadas en ~/vpsdesk-backups sin descargar su contenido.</summary>
    Task<IReadOnlyList<AppBackupInfo>> ListRemoteAsync(ServerProfile server, string? secret, CancellationToken cancellationToken = default);

    /// <summary>Descarga una copia remota y verifica el archivo y su SHA-256 antes de publicarla localmente.</summary>
    Task<AppBackupInfo> DownloadAsync(ServerProfile server, AppBackupInfo backup, string? secret, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Comprueba los paquetes comprimidos y la huella SHA-256 cuando está disponible.</summary>
    Task VerifyAsync(ServerProfile server, AppBackupInfo backup, string? secret, CancellationToken cancellationToken = default);

    /// <summary>Elimina una copia de la carpeta de respaldos del servidor indicado y su huella asociada.</summary>
    Task DeleteAsync(ServerProfile server, AppBackupInfo backup, string? secret, CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea la copia en el VPS, la descarga, verifica su SHA-256 y solo entonces
    /// elimina las copias locales del mismo tipo que excedan <see cref="AppBackupRequest.KeepCopies"/>.
    /// Lanza si algo falla: quien despliega no debe continuar sin copia.
    /// </summary>
    Task<AppBackupResult> CreateAsync(
        AppBackupRequest request,
        string? secret,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Contenedores PostgreSQL activos y las bases de datos del contenedor elegido
    /// (o del primero detectado). Solo lectura.
    /// </summary>
    Task<PostgresDiscovery> DiscoverPostgresAsync(ServerProfile server, string? container, string? secret, CancellationToken cancellationToken = default);

    /// <summary>Bases de datos que se pueden restaurar desde la copia (volcados por base).</summary>
    Task<IReadOnlyList<string>> ListBackupDatabasesAsync(ServerProfile server, AppBackupInfo backup, string? secret, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restaura una base (o todas las de la copia) en el contenedor PostgreSQL. Cada base se
    /// restaura en una sola transacción: si falla, esa base queda como estaba.
    /// </summary>
    Task<DatabaseRestoreResult> RestoreDatabaseAsync(
        DatabaseRestoreRequest request,
        string? secret,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Qué incluye una copia. Se refleja en el nombre del archivo para listarlas sin abrirlas.</summary>
public enum AppBackupContent
{
    /// <summary>Base de datos, proyecto y configuración del sistema.</summary>
    Full = 0,
    /// <summary>Solo los volcados de PostgreSQL.</summary>
    DatabaseOnly = 1,
    /// <summary>Solo el proyecto y la configuración del sistema, sin base de datos.</summary>
    FilesOnly = 2
}

public static class AppBackupNames
{
    public const string Extension = ".vpsbackup.tar";
    public const string DatabaseSuffix = "-db";
    public const string FilesSuffix = "-files";

    public static string SuffixOf(AppBackupContent content) => content switch
    {
        AppBackupContent.DatabaseOnly => DatabaseSuffix,
        AppBackupContent.FilesOnly => FilesSuffix,
        _ => string.Empty
    };

    public static AppBackupContent ContentOf(string fileName)
    {
        if (fileName.EndsWith(DatabaseSuffix + Extension, StringComparison.Ordinal)) return AppBackupContent.DatabaseOnly;
        if (fileName.EndsWith(FilesSuffix + Extension, StringComparison.Ordinal)) return AppBackupContent.FilesOnly;
        return AppBackupContent.Full;
    }
}

/// <param name="KeepOnServerOnly">
/// Modo ahorro de datos: la copia no se descarga al dispositivo, queda en el VPS
/// (~/vpsdesk-backups) con la misma rotación de <paramref name="KeepCopies"/>.
/// </param>
/// <param name="Database">Base concreta a volcar; null vuelca todas.</param>
public sealed record AppBackupRequest(
    ServerProfile Server,
    string RemoteRepositoryPath,
    int KeepCopies = 3,
    string? PostgresContainer = null,
    bool KeepOnServerOnly = false,
    AppBackupContent Content = AppBackupContent.Full,
    string? Database = null);

public sealed record AppBackupInfo(
    string FileName,
    string FullPath,
    long SizeBytes,
    DateTimeOffset CreatedAt,
    bool StoredOnServer = false)
{
    public AppBackupContent Content => AppBackupNames.ContentOf(FileName);
    public bool IncludesDatabase => Content != AppBackupContent.FilesOnly;
}

public sealed record AppBackupResult(
    AppBackupInfo Backup,
    bool DatabaseIncluded,
    IReadOnlyList<string> Warnings,
    int PrunedCount,
    bool StoredOnServer = false);

/// <param name="Container">Contenedor usado para listar las bases (el pedido o el primero detectado).</param>
public sealed record PostgresDiscovery(
    IReadOnlyList<string> Containers,
    string? Container,
    IReadOnlyList<string> Databases);

/// <param name="Database">Base a restaurar; null restaura todas las que contiene la copia.</param>
public sealed record DatabaseRestoreRequest(
    ServerProfile Server,
    AppBackupInfo Backup,
    string? PostgresContainer = null,
    string? Database = null);

public sealed record DatabaseRestoreResult(
    IReadOnlyList<string> RestoredDatabases,
    IReadOnlyList<string> Warnings);
