using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Desktop.Localization;
using VpsDesk.Domain.Activity;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Desktop.ViewModels;

/// <summary>
/// Contexto que la pantalla de despliegues muestra de un vistazo (servidor, entorno, protecciones),
/// datos estructurados de la confirmación y la pila de capas (modales y hojas) que el botón
/// «atrás» de Android debe cerrar de la más alta a la más baja.
/// </summary>
public partial class DeploymentsViewModel : VpsDesk.Desktop.Services.IBackDismissible
{
    [ObservableProperty] private string _activeServerName = string.Empty;
    [ObservableProperty] private string _environmentLabel = string.Empty;
    [ObservableProperty] private bool _isProductionServer;
    [ObservableProperty] private bool _isStagingServer;
    [ObservableProperty] private bool _isHistorySheetOpen;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBackupSheetVisible))] private bool _isBackupSheetOpen;

    /// <summary>La hoja de acciones de una copia solo existe mientras haya una copia seleccionada.</summary>
    public bool IsBackupSheetVisible => IsBackupSheetOpen && SelectedAppBackup is not null;
    [ObservableProperty] private string _pendingExecution = string.Empty;

    /// <summary>Avisos de la confirmación: qué se hará antes de tocar el servidor.</summary>
    public ObservableCollection<string> PendingNotices { get; } = new();

    public bool HasPendingNotices => PendingNotices.Count > 0;
    public bool HasPreflightPassed => PreflightPassedCount > 0;
    public bool HasPreflightWarnings => PreflightWarningCount > 0;
    public bool HasPreflightFailures => PreflightFailedCount > 0;
    public bool HasPostflightPassed => PostflightPassedCount > 0;
    public bool HasPostflightWarnings => PostflightWarningCount > 0;
    public bool HasPostflightFailures => PostflightFailedCount > 0;

    public bool HasActiveServer => !string.IsNullOrEmpty(ActiveServerName);
    public bool HasHistory => DeploymentHistory.Count > 0;
    public bool HasNoHistory => !HasHistory;

    // Protecciones activas para el próximo pase (se muestran como chips, no como texto suelto).
    public bool HasBackupProtection => BackupBeforeDeploy && IsAppBackupConfigured;
    public bool HasSnapshotProtection => SnapshotBeforeDeploy && IsSnapshotConfigured;
    public bool HasDataSaverProtection => IsDataSaverActive;

    partial void OnActiveServerNameChanged(string value) => OnPropertyChanged(nameof(HasActiveServer));
    partial void OnBackupBeforeDeployChanged(bool value) => NotifyProtectionsChanged();
    partial void OnSnapshotBeforeDeployChanged(bool value) => NotifyProtectionsChanged();

    private void NotifyProtectionsChanged()
    {
        OnPropertyChanged(nameof(HasBackupProtection));
        OnPropertyChanged(nameof(HasSnapshotProtection));
        OnPropertyChanged(nameof(HasDataSaverProtection));
    }

    /// <summary>Relee servidor y entorno activos; el entorno de producción se señala siempre.</summary>
    private void RefreshServerContext() => RefreshServerContext(_serverAccessor());

    private void RefreshServerContext(ServerProfile? server)
    {
        ActiveServerName = server?.Name ?? string.Empty;
        IsProductionServer = server?.Environment == ServerEnvironment.Production;
        IsStagingServer = server?.Environment == ServerEnvironment.Staging;
        EnvironmentLabel = server?.Environment switch
        {
            ServerEnvironment.Production => LocalizationService.T("Deployments_EnvProd"),
            ServerEnvironment.Staging => LocalizationService.T("Deployments_EnvStaging"),
            ServerEnvironment.Development => LocalizationService.T("Deployments_EnvDev"),
            _ => string.Empty
        };
        NotifyProtectionsChanged();
    }

    [RelayCommand]
    private void OpenHistoryEntry(OperationHistoryEntry? entry)
    {
        if (entry is null) return;
        SelectedHistoryEntry = entry;
        IsHistorySheetOpen = true;
    }

    [RelayCommand]
    private void CloseHistorySheet() => IsHistorySheetOpen = false;

    /// <summary>Toca una copia del inventario: la selecciona y abre sus acciones (restaurar, descargar…).</summary>
    [RelayCommand]
    private void OpenBackupSheet(VpsDesk.Application.AppBackups.AppBackupInfo? backup)
    {
        if (backup is null) return;
        SelectedAppBackup = backup;
        IsBackupSheetOpen = true;
    }

    [RelayCommand]
    private void CloseBackupSheet() => IsBackupSheetOpen = false;

    /// <summary>
    /// Cierra la capa superior abierta (modal o hoja) y devuelve true si había alguna. Lo usa el
    /// botón «atrás» del teléfono y el toque en el fondo: ninguna capa puede quedar sin salida.
    /// Un prevuelo en curso se cancela; una operación en segundo plano solo se oculta.
    /// </summary>
    public bool TryDismissTopLayer()
    {
        if (IsOperationFeedbackModalOpen) { CloseOperationFeedbackModal(); return true; }
        if (HasPendingSnapshotAction) { CancelSnapshotAction(); return true; }
        if (HasPendingDatabaseRestore) { CancelDatabaseRestore(); return true; }
        if (HasPendingBackupDeletion) { CancelBackupDeletion(); return true; }
        if (HasPendingDeploy) { CancelPendingDeploy(); return true; }
        if (IsPreflightModalOpen) { ClosePreflightModal(); return true; }
        if (IsPostflightModalOpen) { ClosePostflightModal(); return true; }
        if (IsBackupSheetOpen) { CloseBackupSheet(); return true; }
        if (IsHistorySheetOpen) { CloseHistorySheet(); return true; }
        if (IsAdvancedOptionsOpen) { CloseAdvancedOptions(); return true; }
        return false;
    }
}
