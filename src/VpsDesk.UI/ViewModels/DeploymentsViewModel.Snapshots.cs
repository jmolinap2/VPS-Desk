using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Application.Snapshots;
using VpsDesk.Desktop.Services;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Desktop.ViewModels;

/// <summary>
/// Punto de restauración del VPS completo (snapshot del proveedor). Es el
/// seguro de "todo o nada" para pases de riesgo, por ejemplo de madrugada:
/// antes de desplegar se crea el snapshot y, si algo sale mal dentro de la
/// ventana corta, se restaura el servidor entero. Pasada esa ventana ya hay
/// datos reales nuevos, y restaurar los perdería: entonces conviene
/// retroceder solo el código.
/// </summary>
public partial class DeploymentsViewModel
{
    private enum SnapshotAction { Create, Restore }

    private IVpsSnapshotService? _snapshots;
    private SnapshotAction _pendingSnapshotAction;
    private VpsSnapshotInfo? _currentSnapshot;
    private Guid? _pendingSnapshotServerId;
    private long? _pendingSnapshotId;
    public ObservableCollection<VpsBackupInfo> ProviderBackups { get; } = new();

    [ObservableProperty] private bool _snapshotBeforeDeploy;
    [ObservableProperty] private bool _hasSnapshot;
    [ObservableProperty] private bool _isSnapshotBusy;
    [ObservableProperty] private bool _hasPendingSnapshotAction;
    [ObservableProperty] private string _pendingSnapshotTitle = string.Empty;
    [ObservableProperty] private string _pendingSnapshotMessage = string.Empty;
    [ObservableProperty] private string _pendingSnapshotConfirmLabel = string.Empty;
    [ObservableProperty] private bool _pendingSnapshotRequiresPhrase;
    [ObservableProperty] private string _snapshotConfirmPhrase = string.Empty;
    [ObservableProperty] private string _lastSnapshotText = string.Empty;
    [ObservableProperty] private string _snapshotSummary = "Consultando el estado del snapshot...";

    public bool IsSnapshotConfigured => _snapshots?.IsConfigured == true;
    public bool CanUseSnapshots => IsSnapshotConfigured && _serverAccessor() is not null && !IsBusy && !IsSnapshotBusy && !IsAppBackupBusy;
    public bool CanRestoreSnapshot => CanUseSnapshots && HasSnapshot;
    public bool HasProviderBackups => ProviderBackups.Count > 0;
    public bool CanConfirmSnapshotAction => HasPendingSnapshotAction
        && (!PendingSnapshotRequiresPhrase
            || string.Equals(SnapshotConfirmPhrase.Trim(), RestorePhrase, StringComparison.OrdinalIgnoreCase));

    partial void OnSnapshotConfirmPhraseChanged(string value) => OnPropertyChanged(nameof(CanConfirmSnapshotAction));

    partial void OnPendingSnapshotRequiresPhraseChanged(bool value) => OnPropertyChanged(nameof(CanConfirmSnapshotAction));

    partial void OnHasPendingSnapshotActionChanged(bool value) => OnPropertyChanged(nameof(CanConfirmSnapshotAction));

    partial void OnIsSnapshotBusyChanged(bool value) => NotifySnapshotAvailability();

    partial void OnHasSnapshotChanged(bool value) => NotifySnapshotAvailability();

    private void NotifySnapshotAvailability()
    {
        OnPropertyChanged(nameof(CanUseSnapshots));
        OnPropertyChanged(nameof(CanRestoreSnapshot));
        OnPropertyChanged(nameof(CanUseAppBackups));
        NotifyBackupSelectionAvailability();
    }

    public void InitializeSnapshots(IVpsSnapshotService service)
    {
        _snapshots = service;
        ProviderBackups.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasProviderBackups));
        OnPropertyChanged(nameof(IsSnapshotConfigured));
        NotifySnapshotAvailability();

        // Si hay token, por defecto cada despliegue crea su punto de restauración.
        var preferences = _serverAccessor() is { } server ? _profileStore.Find(server.Id) : null;
        SnapshotBeforeDeploy = preferences?.SnapshotBeforeDeploy ?? service.IsConfigured;
        _ = RefreshSnapshotAsync();
    }

    [RelayCommand]
    private async Task RefreshSnapshotAsync()
    {
        if (IsBusy || IsSnapshotBusy || IsAppBackupBusy) return;
        if (_snapshots is not { IsConfigured: true })
        {
            ProviderBackups.Clear();
            _currentSnapshot = null;
            HasSnapshot = false;
            SnapshotSummary = "Snapshots sin configurar. Agrega HOSTINGER_API_TOKEN al .env para habilitarlos.";
            return;
        }

        if (_serverAccessor() is not { } server)
        {
            ProviderBackups.Clear();
            _currentSnapshot = null;
            HasSnapshot = false;
            SnapshotSummary = "Selecciona un servidor para consultar su snapshot.";
            return;
        }

        IsSnapshotBusy = true;
        ProviderBackups.Clear();
        try
        {
            var snapshot = await _snapshots.GetAsync(server.Host);
            if (_serverAccessor()?.Id != server.Id) return;
            ApplySnapshot(snapshot);
            await AppendProviderBackupsNoteAsync(server);
        }
        catch (Exception ex)
        {
            if (_serverAccessor()?.Id != server.Id) return;
            _currentSnapshot = null;
            HasSnapshot = false;
            SnapshotSummary = $"No se pudo consultar el snapshot: {ex.Message}";
        }
        finally
        {
            IsSnapshotBusy = false;
        }
    }

    /// <summary>
    /// Añade al resumen los backups automáticos de Hostinger. Es informativo: si la
    /// consulta falla no debe ocultar el estado del snapshot.
    /// </summary>
    private async Task AppendProviderBackupsNoteAsync(ServerProfile server)
    {
        try
        {
            var backups = await _snapshots!.ListBackupsAsync(server.Host);
            if (_serverAccessor()?.Id != server.Id) return;
            ProviderBackups.Clear();
            foreach (var backup in backups) ProviderBackups.Add(backup);
            SnapshotSummary += backups.Count == 0
                ? " · Hostinger no tiene backups automáticos de este VPS."
                : $" · Backups automáticos de Hostinger: {backups.Count} (último {backups[0].CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm}; se restauran desde hPanel y no son descargables).";
        }
        catch (Exception ex)
        {
            if (_serverAccessor()?.Id != server.Id) return;
            ProviderBackups.Clear();
            SnapshotSummary += $" · No se pudieron consultar los backups de Hostinger: {ex.Message}";
        }
    }

    private void ApplySnapshot(VpsSnapshotInfo? info)
    {
        _currentSnapshot = info;
        HasSnapshot = info is not null;
        LastSnapshotText = info is null ? string.Empty
            : $"{info.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm}"
              + (info.ExpiresAt is { } expires ? $" · vence {expires.ToLocalTime():dd/MM/yyyy}" : string.Empty);
        SnapshotSummary = info is null
            ? "Este VPS no tiene snapshot. Crea uno antes de un despliegue de riesgo."
            : $"Último snapshot: {info.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm}"
              + (info.ExpiresAt is { } exp ? $" · vence {exp.ToLocalTime():dd/MM/yyyy}" : string.Empty)
              + (info.EstimatedRestoreTime is { } rt ? $" · restaurar tarda ~{Math.Max(1, (int)Math.Round(rt.TotalMinutes))} min" : string.Empty);
    }

    [RelayCommand]
    private void RequestCreateSnapshot()
    {
        if (!CanUseSnapshots) return;

        _pendingSnapshotAction = SnapshotAction.Create;
        PendingSnapshotRequiresPhrase = false;
        SnapshotConfirmPhrase = string.Empty;
        _pendingSnapshotServerId = _serverAccessor()?.Id;
        PendingSnapshotTitle = "Crear snapshot del servidor";
        PendingSnapshotConfirmLabel = "Crear snapshot";
        PendingSnapshotMessage = _currentSnapshot is { } existing
            ? $"Hostinger conserva un solo snapshot por VPS: este REEMPLAZARÁ al del {existing.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm} y ese punto de restauración se perderá. Para tener varias copias descargables usa «Copia de la aplicación»."
            : "Se creará un snapshot de todo el VPS. Caduca al día siguiente y Hostinger solo conserva uno. Puede tardar varios minutos; el servidor sigue funcionando mientras tanto.";
        HasPendingSnapshotAction = true;
    }

    [RelayCommand]
    private void RequestRestoreSnapshot()
    {
        if (!CanRestoreSnapshot || _currentSnapshot is not { } snapshot) return;

        _pendingSnapshotAction = SnapshotAction.Restore;
        PendingSnapshotRequiresPhrase = true;
        SnapshotConfirmPhrase = string.Empty;
        _pendingSnapshotServerId = _serverAccessor()?.Id;
        _pendingSnapshotId = snapshot.Id;
        PendingSnapshotTitle = "Restaurar el servidor completo";
        PendingSnapshotConfirmLabel = "Restaurar servidor";
        PendingSnapshotMessage =
            $"El VPS completo volverá al estado del {snapshot.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm}: aplicaciones, contenedores y las bases de datos de TODOS los tenants. "
            + "Se perderá todo lo registrado desde ese momento y el servidor quedará fuera de servicio durante la restauración"
            + (snapshot.EstimatedRestoreTime is { } rt ? $" (estimado ~{Math.Max(1, (int)Math.Round(rt.TotalMinutes))} min)" : string.Empty)
            + ". VPS Desk perderá la conexión SSH. Si ya hay datos reales nuevos, considera retroceder solo el código.";
        HasPendingSnapshotAction = true;
    }

    [RelayCommand]
    private void CancelSnapshotAction()
    {
        HasPendingSnapshotAction = false;
        PendingSnapshotMessage = string.Empty;
        SnapshotConfirmPhrase = string.Empty;
    }

    [RelayCommand]
    private async Task ConfirmSnapshotActionAsync()
    {
        if (!CanConfirmSnapshotAction || !CanUseSnapshots) return;

        var action = _pendingSnapshotAction;
        var serverId = _pendingSnapshotServerId;
        var snapshotId = _pendingSnapshotId;
        CancelSnapshotAction();

        if (_snapshots is not { IsConfigured: true } || _serverAccessor() is not { } server || server.Id != serverId) return;
        if (action == SnapshotAction.Restore && (!CanRestoreSnapshot || _currentSnapshot?.Id != snapshotId)) return;

        if (action == SnapshotAction.Create)
        {
            await CreateSnapshotWithFeedbackAsync(
                server,
                "Creando snapshot del servidor",
                "Hostinger está creando el snapshot. Puede tardar varios minutos.",
                "Snapshot creado. Ya existe un punto de restauración del servidor completo.",
                closeOnSuccess: false);
            return;
        }

        await RestoreSnapshotWithFeedbackAsync(server);
    }

    /// <summary>
    /// Línea para la confirmación del despliegue: dice, antes de aceptar, si se
    /// creará un snapshot (y cuál reemplaza) o si el servidor quedará sin punto
    /// de restauración.
    /// </summary>
    private string BuildSnapshotNotice()
    {
        if (!IsSnapshotConfigured)
        {
            return " Snapshots sin configurar: no habrá punto de restauración del servidor.";
        }

        if (!SnapshotBeforeDeploy)
        {
            return " Sin snapshot previo: no habrá punto de restauración del servidor.";
        }

        return _currentSnapshot is { } existing
            ? $" Antes de desplegar se creará un snapshot del VPS (reemplaza al del {existing.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm})."
            : " Antes de desplegar se creará un snapshot del VPS.";
    }

    /// <summary>
    /// Paso previo al despliegue. Devuelve false si el snapshot era obligatorio
    /// y no se pudo crear: sin punto de restauración no se toca el servidor.
    /// </summary>
    private async Task<bool> TakeSnapshotBeforeDeployAsync(ServerProfile server)
    {
        if (!SnapshotBeforeDeploy || _snapshots is not { IsConfigured: true }) return true;

        CancelPendingDeploy();
        var created = await CreateSnapshotWithFeedbackAsync(
            server,
            "Creando snapshot previo al despliegue",
            "Punto de restauración antes de tocar el servidor. El despliegue empieza al terminar.",
            "Snapshot creado. Iniciando el despliegue...",
            closeOnSuccess: true);

        if (!created)
        {
            StatusMessage = "Despliegue cancelado: no se pudo crear el snapshot previo. No se modificó el servidor.";
            Toast.Error(StatusMessage, "Despliegue cancelado");
        }

        return created;
    }

    private async Task<bool> CreateSnapshotWithFeedbackAsync(
        ServerProfile server,
        string title,
        string startMessage,
        string successMessage,
        bool closeOnSuccess)
    {
        IsSnapshotBusy = true;
        BeginOperationFeedback(title, startMessage);
        var progress = new Progress<string>(AppendSnapshotLog);

        try
        {
            await _snapshots!.CreateAsync(server.Host, progress);
            var snapshot = await _snapshots.GetAsync(server.Host);
            if (_serverAccessor()?.Id == server.Id) ApplySnapshot(snapshot);
            CompleteOperationFeedback(true, successMessage);
            if (closeOnSuccess) IsOperationFeedbackModalOpen = false;
            return true;
        }
        catch (Exception ex)
        {
            AppendSnapshotLog($"ERROR: {ex.Message}");
            CompleteOperationFeedback(false, $"No se pudo crear el snapshot: {UserFacingErrors.Describe(ex)}");
            return false;
        }
        finally
        {
            IsSnapshotBusy = false;
        }
    }

    private async Task RestoreSnapshotWithFeedbackAsync(ServerProfile server)
    {
        IsSnapshotBusy = true;
        BeginOperationFeedback(
            "Restaurando el servidor",
            "Hostinger está restaurando el VPS completo. El servidor estará fuera de servicio hasta que termine.");
        var progress = new Progress<string>(AppendSnapshotLog);

        try
        {
            await _snapshots!.RestoreAsync(server.Host, progress);
            CompleteOperationFeedback(
                true,
                "Restauración completada. Espera a que el servidor termine de arrancar y actualiza el Panel para confirmar que los contenedores están arriba.");
        }
        catch (Exception ex)
        {
            AppendSnapshotLog($"ERROR: {ex.Message}");
            CompleteOperationFeedback(false, $"La restauración no terminó correctamente: {UserFacingErrors.Describe(ex)} Revisa el estado del VPS en hPanel.");
        }
        finally
        {
            IsSnapshotBusy = false;
        }
    }

    private void AppendSnapshotLog(string line)
    {
        _operationFeedbackLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] {line}");
        OperationFeedbackOutput = _operationFeedbackLog.ToString().TrimEnd();
    }
}
