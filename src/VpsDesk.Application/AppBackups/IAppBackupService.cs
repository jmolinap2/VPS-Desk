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

    /// <summary>
    /// Crea la copia en el VPS, la descarga, verifica su SHA-256 y solo entonces
    /// elimina las copias locales que excedan <see cref="AppBackupRequest.KeepCopies"/>.
    /// Lanza si algo falla: quien despliega no debe continuar sin copia.
    /// </summary>
    Task<AppBackupResult> CreateAsync(
        AppBackupRequest request,
        string? secret,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <param name="KeepOnServerOnly">
/// Modo ahorro de datos: la copia no se descarga al dispositivo, queda en el VPS
/// (~/vpsdesk-backups) con la misma rotación de <paramref name="KeepCopies"/>.
/// </param>
public sealed record AppBackupRequest(
    ServerProfile Server,
    string RemoteRepositoryPath,
    int KeepCopies = 3,
    string? PostgresContainer = null,
    bool KeepOnServerOnly = false);

public sealed record AppBackupInfo(
    string FileName,
    string FullPath,
    long SizeBytes,
    DateTimeOffset CreatedAt);

public sealed record AppBackupResult(
    AppBackupInfo Backup,
    bool DatabaseIncluded,
    IReadOnlyList<string> Warnings,
    int PrunedCount,
    bool StoredOnServer = false);
