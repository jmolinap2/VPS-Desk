namespace VpsDesk.Application.Snapshots;

/// <summary>
/// Snapshot de todo el VPS (imágenes, volúmenes, bases de datos y
/// configuración) a nivel de proveedor. Es el seguro de "todo o nada" para
/// despliegues riesgosos, no un retroceso fino: restaurar devuelve el
/// servidor completo al pasado y pierde los datos creados desde entonces.
/// </summary>
public interface IVpsSnapshotService
{
    /// <summary>False si falta el token del proveedor: la UI lo explica en vez de fallar.</summary>
    bool IsConfigured { get; }

    /// <summary>El snapshot actual del VPS, o null si no tiene ninguno.</summary>
    Task<VpsSnapshotInfo?> GetAsync(string serverHost, CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea el snapshot y espera a que el proveedor lo termine. Sobrescribe el
    /// anterior. Lanza si falla o excede el tiempo: quien despliega no debe
    /// continuar sin punto de restauración.
    /// </summary>
    Task CreateAsync(string serverHost, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Restaura el VPS completo al snapshot y espera a que el proveedor termine.</summary>
    Task RestoreAsync(string serverHost, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Backups automáticos del proveedor (semanales, y diarios si el plan los incluye),
    /// de la más reciente a la más antigua. Son de solo lectura aquí: se restauran desde hPanel.
    /// </summary>
    Task<IReadOnlyList<VpsBackupInfo>> ListBackupsAsync(string serverHost, CancellationToken cancellationToken = default);
}

public sealed record VpsBackupInfo(
    long Id,
    DateTimeOffset CreatedAt,
    long? SizeKilobytes,
    TimeSpan? EstimatedRestoreTime);

public sealed record VpsSnapshotInfo(
    long Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    TimeSpan? EstimatedRestoreTime);
