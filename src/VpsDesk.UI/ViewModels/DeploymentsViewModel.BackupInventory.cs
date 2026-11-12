using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Application.AppBackups;
using VpsDesk.Desktop.Services;
using VpsDesk.Desktop.Localization;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Desktop.ViewModels;

public partial class DeploymentsViewModel
{
    public SettingsViewModel? LocalBackupSettings { get; set; }
    private CancellationTokenSource? _appBackupCancellation;
    private ServerProfile? _pendingBackupServer;
    private AppBackupInfo? _pendingBackupDeletion;
    private Guid? _backupInventoryServerId;
    private Guid? _backupContextServerId;

    public ObservableCollection<AppBackupInfo> LocalAppBackups { get; } = new();
    public ObservableCollection<AppBackupInfo> RemoteAppBackups { get; } = new();
    public IEnumerable<AppBackupInfo> AppBackupInventory => LocalAppBackups.Concat(RemoteAppBackups);
    public bool HasAppBackupInventory => LocalAppBackups.Count + RemoteAppBackups.Count > 0;

    [ObservableProperty] private AppBackupInfo? _selectedAppBackup;
    [ObservableProperty] private string _backupInventoryStatus = string.Empty;
    [ObservableProperty] private bool _hasPendingBackupDeletion;
    [ObservableProperty] private string _backupDeletionMessage = string.Empty;

    public bool CanManageSelectedBackup => CanUseAppBackups && SelectedAppBackup is not null
        && _serverAccessor()?.Id == _backupInventoryServerId;
    public bool CanDownloadSelectedBackup => CanManageSelectedBackup && SelectedAppBackup?.StoredOnServer == true;
    public bool CanExportSelectedBackup => CanManageSelectedBackup && SelectedAppBackup?.StoredOnServer == false;

    partial void OnSelectedAppBackupChanged(AppBackupInfo? value) => NotifyBackupSelectionAvailability();

    [RelayCommand]
    private void SaveBackupPreferences()
    {
        if (!CanUseAppBackups || _serverAccessor() is not { } server) return;
        if (string.IsNullOrWhiteSpace(RemoteRepositoryPath))
        {
            Toast.Warning(LocalizationService.T("Backups_PathRequired"));
            return;
        }
        try
        {
            var existing = _activeProjectId is { } projectId
                ? _profileStore.Find(server.Id, projectId)
                : _profileStore.Find(server.Id);
            var profile = existing ?? CreateDeploymentProfile(server);
            _profileStore.Upsert(profile with
            {
                RemoteRepositoryPath = RemoteRepositoryPath.Trim(),
                BackupBeforeDeploy = BackupBeforeDeploy,
                BackupKeepCopies = KeepCopies,
                StoreBackupOnServer = StoreAppBackupOnServer,
                SnapshotBeforeDeploy = SnapshotBeforeDeploy,
                BackupPostgresContainer = AppBackupPostgresContainer.Trim()
            });
            Toast.Success(LocalizationService.T("Backups_PreferencesSaved"));
        }
        catch (Exception ex)
        {
            Toast.Error(ex, LocalizationService.T("Common_Save"));
        }
    }

    private void NotifyBackupSelectionAvailability()
    {
        OnPropertyChanged(nameof(CanManageSelectedBackup));
        OnPropertyChanged(nameof(CanDownloadSelectedBackup));
        OnPropertyChanged(nameof(CanExportSelectedBackup));
        OnPropertyChanged(nameof(CanRestoreDatabase));
        OnPropertyChanged(nameof(CanPickDatabase));
    }

    /// <summary>Resumen para la cabecera: la copia más reciente, esté donde esté.</summary>
    public string LastBackupText
    {
        get
        {
            var latest = AppBackupInventory.OrderByDescending(b => b.CreatedAt).FirstOrDefault();
            return latest is null
                ? LocalizationService.T("Backups_NoBackupsYet")
                : $"{latest.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm} · {FormatBytes(latest.SizeBytes)}";
        }
    }

    public string LastBackupWhere
    {
        get
        {
            var latest = AppBackupInventory.OrderByDescending(b => b.CreatedAt).FirstOrDefault();
            return latest is null ? string.Empty
                : LocalizationService.T(latest.StoredOnServer ? "Backups_Remote" : "Backups_Local");
        }
    }

    public string InventoryCountText
        => LocalizationService.Current.Format("Backups_Counts", LocalAppBackups.Count, RemoteAppBackups.Count);

    private void NotifyBackupOverview()
    {
        OnPropertyChanged(nameof(LastBackupText));
        OnPropertyChanged(nameof(LastBackupWhere));
        OnPropertyChanged(nameof(InventoryCountText));
    }

    public void ResetBackupContext()
    {
        if (_backupContextServerId == _serverAccessor()?.Id) return;
        _backupContextServerId = _serverAccessor()?.Id;
        SelectedAppBackup = null;
        LocalAppBackups.Clear();
        RemoteAppBackups.Clear();
        PostgresContainers.Clear();
        PostgresDatabases.Clear();
        SelectedPostgresContainer = null;
        SelectedBackupDatabase = null;
        PostgresStatus = string.Empty;
        CancelDatabaseRestore();
        LastSnapshotText = string.Empty;
        _backupInventoryServerId = null;
        BackupInventoryStatus = string.Empty;
        CancelBackupDeletion();
        CancelSnapshotAction();
        _currentSnapshot = null;
        ProviderBackups.Clear();
        HasSnapshot = false;
        SnapshotSummary = string.Empty;
        RefreshAppBackups();
        OnPropertyChanged(nameof(AppBackupInventory));
        OnPropertyChanged(nameof(HasAppBackupInventory));
        NotifyBackupOverview();
        NotifySnapshotAvailability();
    }

    [RelayCommand]
    private async Task RefreshBackupInventoryAsync()
    {
        if (!CanUseAppBackups || _serverAccessor() is not { } server) return;
        IsAppBackupBusy = true;
        try
        {
            await LoadBackupInventoryAsync(server, _secretAccessor(), CancellationToken.None);
        }
        finally
        {
            IsAppBackupBusy = false;
        }
    }

    private async Task LoadBackupInventoryAsync(ServerProfile server, string? secret, CancellationToken cancellationToken)
    {
        if (_serverAccessor()?.Id != server.Id) return;
        SelectedAppBackup = null;
        _backupInventoryServerId = server.Id;
        RefreshAppBackups();
        RemoteAppBackups.Clear();
        OnPropertyChanged(nameof(AppBackupInventory));
        OnPropertyChanged(nameof(HasAppBackupInventory));
        NotifyBackupOverview();
        NotifyBackupSelectionAvailability();
        BackupInventoryStatus = "Consultando las copias del VPS…";
        try
        {
            var backups = await _appBackups!.ListRemoteAsync(server, secret, cancellationToken);
            if (_serverAccessor()?.Id != server.Id) return;
            foreach (var backup in backups) RemoteAppBackups.Add(backup);
            BackupInventoryStatus = InventoryCountText;
        }
        catch (Exception ex)
        {
            if (_serverAccessor()?.Id == server.Id)
                BackupInventoryStatus = $"Copias locales disponibles. No se pudo consultar el VPS. {UserFacingErrors.Describe(ex)}";
        }
        OnPropertyChanged(nameof(AppBackupInventory));
        OnPropertyChanged(nameof(HasAppBackupInventory));
        NotifyBackupOverview();
        NotifyBackupSelectionAvailability();
    }

    [RelayCommand]
    private async Task VerifySelectedBackupAsync()
    {
        if (!CanManageSelectedBackup || _serverAccessor() is not { } server || SelectedAppBackup is not { } backup) return;
        await RunBackupManagementAsync("Verificar respaldo", async token =>
        {
            await _appBackups!.VerifyAsync(server, backup, _secretAccessor(), token);
            AppendSnapshotLog(backup.FullPath);
            return "Integridad comprobada. Los paquetes comprimidos se pueden leer. Las copias que tienen huella SHA-256 también se compararon con ella.";
        });
    }

    [RelayCommand]
    private async Task DownloadSelectedBackupAsync()
    {
        if (!CanDownloadSelectedBackup || _serverAccessor() is not { } server || SelectedAppBackup is not { } backup) return;
        var secret = _secretAccessor();
        await RunBackupManagementAsync("Descargar respaldo", async token =>
        {
            var downloaded = await _appBackups!.DownloadAsync(server, backup, secret, new Progress<string>(AppendSnapshotLog), token);
            AppendSnapshotLog(downloaded.FullPath);
            await LoadBackupInventoryAsync(server, secret, CancellationToken.None);
            return "Respaldo descargado y verificado con SHA-256.";
        });
    }

    [RelayCommand]
    private void RequestDeleteBackup()
    {
        if (!CanManageSelectedBackup) return;
        _pendingBackupServer = _serverAccessor();
        _pendingBackupDeletion = SelectedAppBackup;
        BackupDeletionMessage = $"Se eliminará permanentemente «{SelectedAppBackup!.FileName}» "
            + (SelectedAppBackup.StoredOnServer ? "del VPS " : "de este dispositivo para ")
            + $"«{_pendingBackupServer!.Name}».";
        HasPendingBackupDeletion = true;
    }

    [RelayCommand]
    private void CancelBackupDeletion()
    {
        HasPendingBackupDeletion = false;
        _pendingBackupDeletion = null;
        _pendingBackupServer = null;
    }

    [RelayCommand]
    private async Task ConfirmDeleteBackupAsync()
    {
        if (!CanUseAppBackups || !HasPendingBackupDeletion
            || _pendingBackupServer is not { } server || _pendingBackupDeletion is not { } backup) return;
        CancelBackupDeletion();
        if (_serverAccessor()?.Id != server.Id) return;
        var secret = _secretAccessor();
        await RunBackupManagementAsync("Eliminar respaldo", async token =>
        {
            await _appBackups!.DeleteAsync(server, backup, secret, token);
            await LoadBackupInventoryAsync(server, secret, CancellationToken.None);
            return "Respaldo eliminado.";
        });
    }

    public async Task ExportSelectedBackupAsync(Stream destination)
    {
        if (!CanExportSelectedBackup || SelectedAppBackup is not { } backup || _serverAccessor() is not { } server) return;
        await RunBackupManagementAsync("Exportar respaldo", async token =>
        {
            await _appBackups!.VerifyAsync(server, backup, null, token);
            await using var source = File.OpenRead(backup.FullPath);
            await source.CopyToAsync(destination, token);
            await destination.FlushAsync(token);
            return "Respaldo exportado.";
        });
    }

    private async Task RunBackupManagementAsync(string title, Func<CancellationToken, Task<string>> operation)
    {
        IsAppBackupBusy = true;
        using var cancellation = new CancellationTokenSource();
        _appBackupCancellation = cancellation;
        BeginOperationFeedback(title, "Operación en curso…");
        try
        {
            CompleteOperationFeedback(true, await operation(cancellation.Token));
        }
        catch (OperationCanceledException)
        {
            CompleteOperationFeedback(false, "Operación cancelada.");
        }
        catch (Exception ex)
        {
            AppendSnapshotLog(UserFacingErrors.Describe(ex));
            CompleteOperationFeedback(false, UserFacingErrors.Describe(ex));
        }
        finally
        {
            _appBackupCancellation = null;
            IsAppBackupBusy = false;
        }
    }

    [RelayCommand]
    private void CancelAppBackupOperation() => _appBackupCancellation?.Cancel();
}
