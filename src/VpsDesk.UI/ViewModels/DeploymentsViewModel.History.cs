using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Application.Activity;
using VpsDesk.Application.Deployments;
using VpsDesk.Desktop.Localization;
using VpsDesk.Domain.Activity;

namespace VpsDesk.Desktop.ViewModels;

public partial class DeploymentsViewModel
{
    private IOperationHistoryStore? _historyStore;
    private bool _deploymentObserved;
    private bool _historyRecordedForCurrentRun;
    private DateTimeOffset _observedDeploymentStartedAt;
    private readonly StringBuilder _liveDeploymentOutput = new();
    private CancellationTokenSource? _preflightCts;
    private bool _preflightCancelledByUser;
    private readonly ConcurrentQueue<DeploymentProgressUpdate> _progressQueue = new();
    private int _progressDrainScheduled;
    private bool _stepStreamedOutput;
    private int _stepOutputStart;
    private const int LiveOutputFlushMilliseconds = 100;

    // Plazo máximo del prevuelo: pasado ese tiempo se abandona y el modal muestra el motivo.
    private static readonly TimeSpan PreflightTimeout = TimeSpan.FromSeconds(45);

    public ObservableCollection<OperationHistoryEntry> DeploymentHistory { get; } = new();

    [ObservableProperty] private OperationHistoryEntry? _selectedHistoryEntry;
    [ObservableProperty] private bool _isHistoryView;
    [ObservableProperty] private bool _isPreflightModalOpen;
    [ObservableProperty] private string _preflightModalMessage = string.Empty;
    [ObservableProperty] private bool _isPreflightRunning;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasPreflightPassed))] private int _preflightPassedCount;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasPreflightWarnings))] private int _preflightWarningCount;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasPreflightFailures))] private int _preflightFailedCount;
    [ObservableProperty] private bool _isPostflightModalOpen;
    [ObservableProperty] private string _postflightModalMessage = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasPostflightPassed))] private int _postflightPassedCount;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasPostflightWarnings))] private int _postflightWarningCount;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasPostflightFailures))] private int _postflightFailedCount;
    [ObservableProperty] private bool _isDeploymentRunning;
    [ObservableProperty] private bool _isDeploymentConfigurationExpanded = true;
    [ObservableProperty] private double _deploymentProgressPercent;
    [ObservableProperty] private string _deploymentProgressText = string.Empty;
    [ObservableProperty] private bool _followDeploymentOutput = true;
    [ObservableProperty] private string _deploymentLastActivity = string.Empty;

    public string OutputFollowLabel => FollowDeploymentOutput ? "Pausar seguimiento" : "Ir a la última salida";
    public string OutputFollowHint => FollowDeploymentOutput
        ? "Siguiendo la salida · pausa para revisar o copiar líneas anteriores"
        : "Seguimiento pausado · la ejecución continúa recibiendo salida";

    partial void OnFollowDeploymentOutputChanged(bool value)
    {
        OnPropertyChanged(nameof(OutputFollowLabel));
        OnPropertyChanged(nameof(OutputFollowHint));
    }

    [RelayCommand]
    private void ToggleOutputFollow() => FollowDeploymentOutput = !FollowDeploymentOutput;

    public bool IsNewDeploymentView => !IsHistoryView;
    public bool HasSelectedHistoryEntry => SelectedHistoryEntry is not null;

    // La vista móvil solo muestra la tarjeta de ejecución (progreso + salida)
    // cuando hay algo que mostrar, para no ocupar pantalla con un panel vacío.
    public bool HasExecutionActivity => IsDeploymentRunning || !string.IsNullOrEmpty(DeploymentOutput);
    public bool IsDeploymentConfigurationCollapsed => !IsDeploymentConfigurationExpanded;
    public event EventHandler? HistoryChanged;

    partial void OnIsHistoryViewChanged(bool value)
        => OnPropertyChanged(nameof(IsNewDeploymentView));

    partial void OnSelectedHistoryEntryChanged(OperationHistoryEntry? value)
        => OnPropertyChanged(nameof(HasSelectedHistoryEntry));

    partial void OnIsDeploymentRunningChanged(bool value)
        => OnPropertyChanged(nameof(HasExecutionActivity));

    partial void OnDeploymentOutputChanged(string value)
        => OnPropertyChanged(nameof(HasExecutionActivity));

    partial void OnIsDeploymentConfigurationExpandedChanged(bool value)
        => OnPropertyChanged(nameof(IsDeploymentConfigurationCollapsed));

    partial void OnStatusMessageChanged(string value)
    {
        if (value.Contains("Deployment is running", StringComparison.OrdinalIgnoreCase)
            || value.Contains("despliegue está en ejecución", StringComparison.OrdinalIgnoreCase))
        {
            _deploymentObserved = true;
            _historyRecordedForCurrentRun = false;
            _observedDeploymentStartedAt = DateTimeOffset.UtcNow;
            IsDeploymentRunning = true;
            IsDeploymentConfigurationExpanded = false;
            DeploymentProgressPercent = 0;
            DeploymentProgressText = "Preparando despliegue...";
            FollowDeploymentOutput = true;
            DeploymentLastActivity = "Esperando la primera respuesta del servidor…";
            _liveDeploymentOutput.Clear();
            _stepStreamedOutput = false;
            while (_progressQueue.TryDequeue(out _)) { }
            DeploymentOutput = $"[{DateTime.Now:HH:mm:ss}] Iniciando despliegue...";
            _deployment.ProgressChanged -= OnDeploymentProgressChanged;
            _deployment.ProgressChanged += OnDeploymentProgressChanged;
        }

        if (value.Contains("Verifying the real container", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Verificando", StringComparison.OrdinalIgnoreCase) && value.Contains("contenedor", StringComparison.OrdinalIgnoreCase))
        {
            DeploymentProgressPercent = 95;
            DeploymentProgressText = "Comandos completados · verificando postvuelo";
            _deployment.ProgressChanged -= OnDeploymentProgressChanged;
            BeginPostflightModal();
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        NotifySnapshotAvailability();
        if (value) return;

        if (IsPostflightModalOpen)
        {
            CompletePostflightModalFromChecks();
        }

        if (_deploymentObserved)
        {
            while (_progressQueue.TryDequeue(out _)) { }
            DeploymentProgressText = DeploymentOutcomeTitle;
        }
        IsDeploymentRunning = false;
        _deployment.ProgressChanged -= OnDeploymentProgressChanged;

        if (_deploymentObserved && !_historyRecordedForCurrentRun)
        {
            _historyRecordedForCurrentRun = true;
            _ = RecordObservedDeploymentAsync();
        }
    }

    public void InitializeHistory(IOperationHistoryStore historyStore)
    {
        _historyStore = historyStore;
        _ = RefreshHistoryAsync();
    }

    [RelayCommand]
    private void ShowNewDeployment()
    {
        if (IsBusy) return;

        IsHistoryView = false;
        Checks.Clear();
        Steps.Clear();
        PostChecks.Clear();
        _liveDeploymentOutput.Clear();
        DeploymentOutput = string.Empty;
        DeploymentProgressPercent = 0;
        DeploymentProgressText = string.Empty;
        CanDeploy = false;
        IsPreflightModalOpen = false;
        IsPostflightModalOpen = false;
        IsDeploymentConfigurationExpanded = string.IsNullOrWhiteSpace(RemoteRepositoryPath);
        CancelPendingDeploy();
        StatusMessage = "Nuevo despliegue preparado. Revisa la configuración y ejecuta el prevuelo.";
    }

    [RelayCommand]
    private async Task ShowHistoryAsync()
    {
        IsHistoryView = true;
        await RefreshHistoryAsync();
    }

    [RelayCommand]
    private void ToggleDeploymentConfiguration()
        => IsDeploymentConfigurationExpanded = !IsDeploymentConfigurationExpanded;

    /// <summary>
    /// Cierra el modal de prevuelo. Si la comprobación sigue en curso la cancela: el usuario nunca
    /// queda atrapado esperando a un servidor que no responde.
    /// </summary>
    [RelayCommand]
    private void ClosePreflightModal()
    {
        if (IsPreflightRunning)
        {
            CancelPreflight();
            return;
        }

        IsPreflightModalOpen = false;
    }

    [RelayCommand]
    private void CancelPreflight()
    {
        _preflightCancelledByUser = true;
        _preflightCts?.Cancel();
    }

    /// <summary>Tras un prevuelo correcto lanzado a solas: cierra el resultado y pide la confirmación.</summary>
    [RelayCommand]
    private void ContinueAfterPreflight()
    {
        IsPreflightModalOpen = false;
        RequestDeploy();
    }

    public bool IsPreflightFinished => IsPreflightModalOpen && !IsPreflightRunning;
    public bool PreflightSucceeded => IsPreflightFinished && CanDeploy;
    public bool PreflightBlocked => IsPreflightFinished && !CanDeploy;

    partial void OnIsPreflightModalOpenChanged(bool value) => NotifyPreflightState();
    partial void OnIsPreflightRunningChanged(bool value) => NotifyPreflightState();
    partial void OnCanDeployChanged(bool value) => NotifyPreflightState();

    private void NotifyPreflightState()
    {
        OnPropertyChanged(nameof(IsPreflightFinished));
        OnPropertyChanged(nameof(PreflightSucceeded));
        OnPropertyChanged(nameof(PreflightBlocked));
    }

    [RelayCommand]
    private void ClosePostflightModal()
    {
        // Cerrar solo oculta el resultado: la verificación sigue y su veredicto queda en pantalla.
        IsPostflightModalOpen = false;
        IsDeploymentConfigurationExpanded = true;
    }

    public async Task RefreshHistoryAsync()
    {
        if (_historyStore is null) return;
        var serverId = _serverAccessor()?.Id;
        var items = await _historyStore.GetDeploymentsAsync(serverId, 100);

        DeploymentHistory.Clear();
        foreach (var item in items) DeploymentHistory.Add(item);
        SelectedHistoryEntry = DeploymentHistory.FirstOrDefault();
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(HasNoHistory));
    }

    private CancellationToken BeginPreflightModal()
    {
        _preflightCts?.Dispose();
        var cts = _preflightCts = new CancellationTokenSource(PreflightTimeout);
        _preflightCancelledByUser = false;
        PreflightPassedCount = 0;
        PreflightWarningCount = 0;
        PreflightFailedCount = 0;
        IsPreflightRunning = true;
        IsPreflightModalOpen = true;
        PreflightModalMessage = LocalizationService.T("Deployments_PreflightRunning");
        return cts.Token;
    }

    private void CountPreflightChecks()
    {
        PreflightPassedCount = Checks.Count(x => x.Status == PreflightCheckStatus.Passed);
        PreflightWarningCount = Checks.Count(x => x.Status == PreflightCheckStatus.Warning);
        PreflightFailedCount = Checks.Count(x => x.Status == PreflightCheckStatus.Failed);
    }

    private void BeginPostflightModal()
    {
        PostflightPassedCount = 0;
        PostflightWarningCount = 0;
        PostflightFailedCount = 0;
        IsPostflightModalOpen = true;
        PostflightModalMessage = "Verificando contenedores y comprobaciones HTTP después del despliegue...";
    }

    private void CompletePostflightModalFromChecks()
    {
        PostflightPassedCount = PostChecks.Count(x => x.Status == PostDeployCheckStatus.Passed);
        PostflightWarningCount = PostChecks.Count(x => x.Status == PostDeployCheckStatus.Warning);
        PostflightFailedCount = PostChecks.Count(x => x.Status == PostDeployCheckStatus.Failed);

        PostflightModalMessage = PostChecks.Count == 0
            ? "No se pudo verificar el estado final. Revisa la conexión y los contenedores antes de repetir el pase."
            : PostflightFailedCount > 0
            ? "El despliegue terminó, pero el postvuelo detectó problemas. Revisa los controles fallidos antes de considerar saludable la versión."
            : PostflightWarningCount > 0
                ? "El despliegue terminó con advertencias de postvuelo. Conviene revisarlas antes de cerrar."
                : "Despliegue verificado. Los contenedores y comprobaciones configuradas respondieron correctamente.";
    }

    // El servicio emite eventos desde hilos de fondo (incluida cada línea de salida).
    // Se encolan y se aplican en orden en el hilo de UI, agrupando las líneas de salida
    // unos milisegundos para no reconstruir todo el texto del terminal por cada línea.
    private void OnDeploymentProgressChanged(DeploymentProgressUpdate update)
    {
        _progressQueue.Enqueue(update);
        ScheduleProgressDrain();
    }

    private void ScheduleProgressDrain()
    {
        if (Interlocked.Exchange(ref _progressDrainScheduled, 1) != 0) return;
        Dispatcher.UIThread.Post(async () => await DrainProgressAsync());
    }

    private async Task DrainProgressAsync()
    {
        if (_progressQueue.TryPeek(out var head) && head.State is DeploymentProgressState.Output or DeploymentProgressState.OutputSnapshot)
        {
            await Task.Delay(LiveOutputFlushMilliseconds);
        }

        var applied = false;
        while (_progressQueue.TryDequeue(out var update))
        {
            ApplyProgress(update);
            applied = true;
        }

        // Si el despliegue ya terminó, la salida final completa reemplaza a la parcial.
        if (applied && IsDeploymentRunning)
        {
            DeploymentOutput = _liveDeploymentOutput.ToString().TrimEnd();
        }

        Interlocked.Exchange(ref _progressDrainScheduled, 0);
        if (!_progressQueue.IsEmpty) ScheduleProgressDrain();
    }

    private void ApplyProgress(DeploymentProgressUpdate update)
    {
        DeploymentLastActivity = $"Última actividad recibida · {DateTime.Now:HH:mm:ss}";
        const string RunningMarker = "Ejecutando...";

        void RemoveRunningMarker()
        {
            var length = _liveDeploymentOutput.Length;
            if (length < RunningMarker.Length) return;

            var start = length - RunningMarker.Length;
            for (var i = 0; i < RunningMarker.Length; i++)
            {
                if (_liveDeploymentOutput[start + i] != RunningMarker[i]) return;
            }

            _liveDeploymentOutput.Length = start;
        }

        if (update.State == DeploymentProgressState.Output)
        {
            RemoveRunningMarker();
            _stepStreamedOutput = true;
            _liveDeploymentOutput.AppendLine(update.Output ?? string.Empty);
            return;
        }

        if (update.State == DeploymentProgressState.OutputSnapshot)
        {
            _stepStreamedOutput = true;
            _liveDeploymentOutput.Length = _stepOutputStart;
            _liveDeploymentOutput.Append(update.Output);
            return;
        }

        var total = Math.Max(1, update.TotalSteps);
        var completedBeforeCurrent = Math.Max(0, update.StepIndex - 1);
        DeploymentProgressPercent = update.State == DeploymentProgressState.Completed
            ? Math.Clamp(update.StepIndex * 90d / total, 0, 90)
            : Math.Clamp(completedBeforeCurrent * 90d / total, 0, 90);
        var label = update.Code switch
        {
            "fetch" => "Buscando cambios en Git",
            "checkout" => "Seleccionando la rama",
            "pull" => "Actualizando el código",
            "compose_pull" => "Descargando imágenes",
            "compose_deps" => "Comprobando dependencias",
            "compose_up" => "Construyendo e iniciando servicios",
            "compose_ps" => "Consultando los contenedores",
            "build_cache_prune" => "Limpiando la caché de construcción",
            _ => update.Label
        };
        DeploymentProgressText = $"Paso {Math.Max(1, update.StepIndex)} de {total} · {label}";

        if (update.State == DeploymentProgressState.Started)
        {
            _stepStreamedOutput = false;
            if (_liveDeploymentOutput.Length > 0) _liveDeploymentOutput.AppendLine();
            _liveDeploymentOutput.AppendLine($"[{DateTime.Now:HH:mm:ss}] ▶ {update.Label}");
            _stepOutputStart = _liveDeploymentOutput.Length;
            _liveDeploymentOutput.Append(RunningMarker);
            return;
        }

        RemoveRunningMarker();

        if (_liveDeploymentOutput.Length > 0 && _liveDeploymentOutput[^1] != '\n')
            _liveDeploymentOutput.AppendLine();
        Steps.Add(new DeploymentStepResult(update.Code, update.Label,
            update.Succeeded == true, update.ExitCode ?? -1, update.Output ?? string.Empty,
            update.Duration ?? TimeSpan.Zero, update.IsBlocking));
        var marker = update.Succeeded == true ? "✓" : update.IsBlocking ? "✕" : "⚠";
        var duration = update.Duration?.TotalSeconds ?? 0;
        _liveDeploymentOutput.AppendLine($"{marker} exit {update.ExitCode ?? -1} · {duration:F1}s");

        // Si las líneas ya se mostraron en vivo no se repiten al cerrar el paso.
        if (!_stepStreamedOutput && !string.IsNullOrWhiteSpace(update.Output))
        {
            _liveDeploymentOutput.AppendLine(update.Output.TrimEnd());
        }
    }

    private async Task RecordObservedDeploymentAsync()
    {
        // La marca se libera siempre: si se borra el servidor activo durante el despliegue,
        // o falla el guardado, el prevuelo siguiente no debe quedar sin cierre ni "Aceptar".
        try
        {
            await RecordObservedDeploymentCoreAsync();
        }
        finally
        {
            _deploymentObserved = false;
        }
    }

    private async Task RecordObservedDeploymentCoreAsync()
    {
        if (_historyStore is null) return;
        var server = _serverAccessor();
        if (server is null) return;

        var hasFailedStep = Steps.Any(x => x.IsBlocking && !x.Succeeded);
        var hasMaintenanceWarning = Steps.Any(x => !x.IsBlocking && !x.Succeeded);
        var hasFailedPostCheck = PostChecks.Any(x => x.Status == PostDeployCheckStatus.Failed);
        var hasWarning = hasMaintenanceWarning || PostChecks.Any(x => x.Status == PostDeployCheckStatus.Warning);
        var statusLooksFailed = StatusMessage.Contains("failed", StringComparison.OrdinalIgnoreCase)
                                || StatusMessage.Contains("fall", StringComparison.OrdinalIgnoreCase)
                                || StatusMessage.Contains("problemas", StringComparison.OrdinalIgnoreCase);

        var outcome = hasFailedStep || hasFailedPostCheck || statusLooksFailed
            ? OperationOutcome.Failed
            : hasWarning
                ? OperationOutcome.Warning
                : OperationOutcome.Success;

        var failedStep = Steps.FirstOrDefault(x => x.IsBlocking && !x.Succeeded)?.Label;
        var finishedAt = DateTimeOffset.UtcNow;
        var startedAt = _observedDeploymentStartedAt == default ? finishedAt : _observedDeploymentStartedAt;
        var details = new StringBuilder();
        foreach (var step in Steps.Where(x => !x.IsBlocking && !x.Succeeded))
        {
            details.AppendLine($"[Warning] {step.Label}: exit {step.ExitCode}");
        }
        foreach (var check in PostChecks)
        {
            details.AppendLine($"[{check.Status}] {check.Label}: {check.Detail}");
        }

        var summary = outcome switch
        {
            OperationOutcome.Success => $"Despliegue correcto · {Branch.Trim()}",
            OperationOutcome.Warning => $"Despliegue con advertencias · {Branch.Trim()}",
            _ => $"Despliegue fallido · {Branch.Trim()} · {failedStep ?? "postvuelo/ejecución"}"
        };

        var entry = new OperationHistoryEntry(
            Guid.NewGuid(),
            server.Id,
            server.Name,
            server.Environment.ToString(),
            OperationKind.Deployment,
            "Deploy",
            summary,
            outcome,
            startedAt,
            finishedAt,
            RemoteRepositoryPath.Trim(),
            Branch.Trim(),
            null,
            null,
            ComposeFile.Trim(),
            failedStep,
            DeploymentOutput,
            details.Length == 0 ? StatusMessage : details.ToString().TrimEnd());

        await _historyStore.AddAsync(entry);
        await RefreshHistoryAsync();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }
}
