using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Application.AppBackups;
using VpsDesk.Desktop.Localization;
using VpsDesk.Desktop.Services;

namespace VpsDesk.Desktop.ViewModels;

/// <summary>
/// Copias por tipo (completa, solo base de datos, solo archivos), selección de contenedor y base
/// de PostgreSQL con combos y restauración de una base desde una copia.
/// </summary>
public partial class DeploymentsViewModel
{
    /// <summary>Frase que hay que escribir para confirmar una restauración (BD o servidor).</summary>
    public const string RestorePhrase = "RESTAURAR";

    private Guid? _postgresServerId;
    private AppBackupInfo? _pendingRestoreBackup;

    public ObservableCollection<string> PostgresContainers { get; } = new();
    public ObservableCollection<string> PostgresDatabases { get; } = new();

    /// <summary>0 = completa, 1 = solo base de datos, 2 = solo archivos (índice del combo).</summary>
    [ObservableProperty] private int _appBackupContentIndex;
    [ObservableProperty] private string? _selectedPostgresContainer;
    /// <summary>Base elegida para la copia; vacío = todas.</summary>
    [ObservableProperty] private string? _selectedBackupDatabase;
    [ObservableProperty] private string _postgresStatus = string.Empty;

    [ObservableProperty] private bool _hasPendingDatabaseRestore;
    [ObservableProperty] private string _databaseRestoreMessage = string.Empty;
    [ObservableProperty] private string _databaseRestoreConfirmation = string.Empty;
    [ObservableProperty] private bool _backupDatabaseBeforeRestore = true;
    [ObservableProperty] private string? _selectedRestoreDatabase;
    public ObservableCollection<string> RestoreDatabases { get; } = new();

    public AppBackupContent AppBackupContentKind => (AppBackupContent)Math.Clamp(AppBackupContentIndex, 0, 2);
    public bool CreateTargetsDatabase => AppBackupContentKind != AppBackupContent.FilesOnly;
    public bool CanPickDatabase => CanUseAppBackups && CreateTargetsDatabase && PostgresDatabases.Count > 0;
    public bool CanRestoreDatabase => CanManageSelectedBackup && SelectedAppBackup?.IncludesDatabase == true;
    public bool CanConfirmDatabaseRestore => HasPendingDatabaseRestore
        && string.Equals(DatabaseRestoreConfirmation.Trim(), RestorePhrase, StringComparison.OrdinalIgnoreCase);

    partial void OnAppBackupContentIndexChanged(int value)
    {
        OnPropertyChanged(nameof(AppBackupContentKind));
        OnPropertyChanged(nameof(CreateTargetsDatabase));
        OnPropertyChanged(nameof(CanPickDatabase));
        if (CreateTargetsDatabase && PostgresDatabases.Count == 0) _ = RefreshPostgresAsync();
    }

    partial void OnDatabaseRestoreConfirmationChanged(string value) => OnPropertyChanged(nameof(CanConfirmDatabaseRestore));

    partial void OnHasPendingDatabaseRestoreChanged(bool value) => OnPropertyChanged(nameof(CanConfirmDatabaseRestore));

    partial void OnSelectedPostgresContainerChanged(string? value)
    {
        // Cambiar de contenedor cambia las bases disponibles; durante la carga se ignora.
        if (_loadingPostgres || value is null) return;
        _ = RefreshPostgresAsync();
    }

    private bool _loadingPostgres;

    [RelayCommand]
    private void ClearBackupDatabase() => SelectedBackupDatabase = null;

    [RelayCommand]
    private async Task RefreshPostgresAsync()
    {
        if (_appBackups is null || _serverAccessor() is not { } server) return;
        if (IsAppBackupBusy || IsBusy) return;

        _loadingPostgres = true;
        PostgresStatus = "Consultando PostgreSQL en el VPS…";
        try
        {
            var requested = string.IsNullOrWhiteSpace(SelectedPostgresContainer)
                ? (string.IsNullOrWhiteSpace(AppBackupPostgresContainer) ? null : AppBackupPostgresContainer.Trim())
                : SelectedPostgresContainer;
            var discovery = await _appBackups.DiscoverPostgresAsync(server, requested, _secretAccessor());
            if (_serverAccessor()?.Id != server.Id) return;

            _postgresServerId = server.Id;
            PostgresContainers.Clear();
            foreach (var container in discovery.Containers) PostgresContainers.Add(container);
            PostgresDatabases.Clear();
            foreach (var database in discovery.Databases) PostgresDatabases.Add(database);
            SelectedPostgresContainer = discovery.Container;
            if (SelectedBackupDatabase is not null && !PostgresDatabases.Contains(SelectedBackupDatabase)) SelectedBackupDatabase = null;
            // Se recuerda en la configuración: el respaldo previo al despliegue usa el mismo contenedor.
            if (discovery.Container is not null && string.IsNullOrWhiteSpace(AppBackupPostgresContainer))
                AppBackupPostgresContainer = discovery.Container;

            PostgresStatus = discovery.Container is null
                ? "No hay contenedores PostgreSQL activos en el VPS."
                : $"{discovery.Container} · {discovery.Databases.Count} base(s) de datos";
        }
        catch (Exception ex)
        {
            if (_serverAccessor()?.Id == server.Id) PostgresStatus = UserFacingErrors.Describe(ex);
        }
        finally
        {
            _loadingPostgres = false;
            OnPropertyChanged(nameof(CanPickDatabase));
        }
    }

    // ── Restauración de una base de datos desde la copia seleccionada ────────────────────

    [RelayCommand]
    private async Task RequestRestoreDatabaseAsync()
    {
        if (!CanRestoreDatabase || _serverAccessor() is not { } server || SelectedAppBackup is not { } backup) return;

        IReadOnlyList<string> names;
        try
        {
            names = await _appBackups!.ListBackupDatabasesAsync(server, backup, _secretAccessor());
        }
        catch (Exception ex)
        {
            Toast.Error(ex, LocalizationService.T("Backups_RestoreDb"));
            return;
        }

        if (names.Count == 0)
        {
            Toast.Warning(LocalizationService.T("Backups_RestoreDbLegacy"));
            return;
        }

        _pendingRestoreBackup = backup;
        RestoreDatabases.Clear();
        foreach (var name in names) RestoreDatabases.Add(name);
        SelectedRestoreDatabase = null;
        DatabaseRestoreConfirmation = string.Empty;
        BackupDatabaseBeforeRestore = true;
        UpdateDatabaseRestoreMessage();
        HasPendingDatabaseRestore = true;
    }

    partial void OnSelectedRestoreDatabaseChanged(string? value) => UpdateDatabaseRestoreMessage();

    private void UpdateDatabaseRestoreMessage()
    {
        if (_pendingRestoreBackup is not { } backup || _serverAccessor() is not { } server) return;
        var target = SelectedRestoreDatabase is { } one ? $"la base «{one}»" : $"las {RestoreDatabases.Count} bases de la copia";
        DatabaseRestoreMessage = $"Se reemplazará {target} en el servidor «{server.Name}» por su estado del "
            + $"{backup.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm}. Lo registrado después de esa fecha se perderá. "
            + "Las conexiones abiertas a esa base se cerrarán un momento y la restauración se hace en una sola transacción: "
            + "si falla, la base queda como estaba.";
    }

    [RelayCommand]
    private void CancelDatabaseRestore()
    {
        HasPendingDatabaseRestore = false;
        _pendingRestoreBackup = null;
        DatabaseRestoreConfirmation = string.Empty;
    }

    [RelayCommand]
    private async Task ConfirmDatabaseRestoreAsync()
    {
        if (!CanConfirmDatabaseRestore || !CanUseAppBackups
            || _pendingRestoreBackup is not { } backup || _serverAccessor() is not { } server) return;

        var database = SelectedRestoreDatabase;
        var container = string.IsNullOrWhiteSpace(SelectedPostgresContainer) ? null : SelectedPostgresContainer;
        var safetyCopy = BackupDatabaseBeforeRestore;
        var secret = _secretAccessor();
        CancelDatabaseRestore();
        if (_serverAccessor()?.Id != server.Id) return;

        await RunBackupManagementAsync("Restaurar base de datos", async token =>
        {
            var progress = new Progress<string>(AppendSnapshotLog);
            if (safetyCopy)
            {
                // Red de seguridad: copia de la base actual que se queda en el VPS, sin descargar.
                AppendSnapshotLog("Creando una copia de seguridad de la base actual antes de restaurar…");
                var safety = await _appBackups!.CreateAsync(
                    new AppBackupRequest(server, string.Empty, KeepCopies, container, KeepOnServerOnly: true,
                        Content: AppBackupContent.DatabaseOnly, Database: database),
                    secret, progress, token);
                AppendSnapshotLog($"Copia de seguridad guardada en el VPS: {safety.Backup.FileName}");
            }

            var result = await _appBackups!.RestoreDatabaseAsync(
                new DatabaseRestoreRequest(server, backup, container, database), secret, progress, token);
            foreach (var warning in result.Warnings) AppendSnapshotLog($"AVISO: {warning}");
            await LoadBackupInventoryAsync(server, secret, CancellationToken.None);
            return $"Base de datos restaurada: {string.Join(", ", result.RestoredDatabases)}.";
        });
    }
}
