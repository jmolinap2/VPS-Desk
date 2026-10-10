using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Application.Activity;
using VpsDesk.Application.Deployments;
using VpsDesk.Domain.Activity;

namespace VpsDesk.Desktop.ViewModels;

public partial class DeploymentsViewModel
{
    private IOperationHistoryStore? _historyStore;
    private bool _deploymentObserved;
    private bool _historyRecordedForCurrentRun;
    private DateTimeOffset _observedDeploymentStartedAt;
    private readonly StringBuilder _liveDeploymentOutput = new();
    private CancellationTokenSource? _preflightCloseCts;
    private bool _isPreflightPointerOver;
    private bool _isPreflightCountdownPinned;
    private bool IsPreflightCountdownPaused => _isPreflightPointerOver || _isPreflightCountdownPinned;
    private readonly ConcurrentQueue<DeploymentProgressUpdate> _progressQueue = new();
    private int _progressDrainScheduled;
    private bool _stepStreamedOutput;
    private const int LiveOutputFlushMilliseconds = 100;

    // Tiempo que el prevuelo exitoso queda visible antes de cerrarse solo.
    // El usuario puede cerrarlo antes con "Aceptar".
    private const int PreflightAutoCloseSeconds = 10;

    public ObservableCollection<OperationHistoryEntry> DeploymentHistory { get; } = new();

    [ObservableProperty] private OperationHistoryEntry? _selectedHistoryEntry;
    [ObservableProperty] private bool _isHistoryView;
    [ObservableProperty] private bool _isPreflightModalOpen;
    [ObservableProperty] private string _preflightModalMessage = string.Empty;
    [ObservableProperty] private bool _hasPreflightCountdown;
    [ObservableProperty] private string _preflightCountdownText = string.Empty;
    [ObservableProperty] private bool _isPostflightModalOpen;
    [ObservableProperty] private string _postflightModalMessage = string.Empty;
    [ObservableProperty] private int _postflightPassedCount;
    [ObservableProperty] private int _postflightWarningCount;
    [ObservableProperty] private int _postflightFailedCount;
    [ObservableProperty] private bool _isDeploymentRunning;
    [ObservableProperty] private bool _isDeploymentConfigurationExpanded = true;
    [ObservableProperty] private double _deploymentProgressPercent;
    [ObservableProperty] private string _deploymentProgressText = string.Empty;

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
        if (value.Contains("preflight", StringComparison.OrdinalIgnoreCase)
            || value.Contains("validación previa", StringComparison.OrdinalIgnoreCase))
        {
            if (IsBusy && Steps.Count == 0)
            {
                BeginPreflightModal();
            }
        }

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
            DeploymentProgressPercent = 100;
            DeploymentProgressText = "Comandos completados · verificando postvuelo";
            IsDeploymentRunning = false;
            _deployment.ProgressChanged -= OnDeploymentProgressChanged;
            BeginPostflightModal();
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        NotifySnapshotAvailability();
        if (value) return;

        if (IsPreflightModalOpen && !_deploymentObserved)
        {
            _ = CompletePreflightModalAsync(CanDeploy);
        }

        if (IsPostflightModalOpen)
        {
            CompletePostflightModalFromChecks();
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
        CancelPreflightCountdown();
        IsPreflightModalOpen = false;
        IsPostflightModalOpen = false;
        IsDeploymentConfigurationExpanded = true;
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

    [RelayCommand]
    private void ClosePreflightModal()
    {
        if (IsBusy) return;

        CancelPreflightCountdown();
        IsPreflightModalOpen = false;
    }

    // Mientras el usuario lee o desplaza la lista, el prevuelo no se cierra solo.
    // El puntero encima pausa; tocar o pulsar dentro lo deja pausado hasta "Aceptar"
    // (en el teléfono no hay "hover": al soltar el dedo el puntero "sale").
    public void SetPreflightPointerOver(bool isOver) => _isPreflightPointerOver = isOver;

    public void PinPreflightCountdown() => _isPreflightCountdownPinned = true;

    [RelayCommand]
    private void ClosePostflightModal()
    {
        if (!IsBusy)
        {
            IsPostflightModalOpen = false;
            IsDeploymentConfigurationExpanded = true;
        }
    }

    public async Task RefreshHistoryAsync()
    {
        if (_historyStore is null) return;
        var serverId = _serverAccessor()?.Id;
        var items = await _historyStore.GetDeploymentsAsync(serverId, 100);

        DeploymentHistory.Clear();
        foreach (var item in items) DeploymentHistory.Add(item);
        SelectedHistoryEntry = DeploymentHistory.FirstOrDefault();
    }

    private void BeginPreflightModal()
    {
        CancelPreflightCountdown();
        _isPreflightPointerOver = false;
        _isPreflightCountdownPinned = false;
        IsPreflightModalOpen = true;
        PreflightModalMessage = "Comprobando SSH, Git, Docker, Compose, rama, archivos y entorno...";
    }

    private async Task CompletePreflightModalAsync(bool passed)
    {
        PreflightModalMessage = passed
            ? "Prevuelo correcto. Todo está listo para continuar."
            : "El prevuelo encontró bloqueos. Revisa los elementos marcados antes de desplegar.";

        // Con bloqueos el modal se queda abierto hasta que el usuario lo cierre.
        if (!passed) return;

        // Antes se cerraba solo a los 0,65 s: imposible de leer, sobre todo en
        // el teléfono. Ahora queda visible con una cuenta regresiva y el botón
        // "Aceptar" para cerrarlo antes.
        CancelPreflightCountdown();
        var cts = _preflightCloseCts = new CancellationTokenSource();
        try
        {
            HasPreflightCountdown = true;
            var remaining = PreflightAutoCloseSeconds;
            while (remaining > 0)
            {
                PreflightCountdownText = IsPreflightCountdownPaused
                    ? "Pausado · pulsa Aceptar para continuar"
                    : $"Se cerrará en {remaining} s";
                await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
                if (!IsPreflightCountdownPaused) remaining--;
            }

            IsPreflightModalOpen = false;
        }
        catch (OperationCanceledException)
        {
            // El usuario lo cerró (o empezó otro prevuelo) antes de tiempo.
        }
        finally
        {
            if (ReferenceEquals(_preflightCloseCts, cts))
            {
                HasPreflightCountdown = false;
                PreflightCountdownText = string.Empty;
            }
        }
    }

    private void CancelPreflightCountdown()
    {
        _preflightCloseCts?.Cancel();
        _preflightCloseCts = null;
        HasPreflightCountdown = false;
        PreflightCountdownText = string.Empty;
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

        PostflightModalMessage = PostflightFailedCount > 0
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
        if (_progressQueue.TryPeek(out var head) && head.State == DeploymentProgressState.Output)
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

        var total = Math.Max(1, update.TotalSteps);
        var completedBeforeCurrent = Math.Max(0, update.StepIndex - 1);
        DeploymentProgressPercent = update.State == DeploymentProgressState.Completed
            ? Math.Clamp(update.StepIndex * 100d / total, 0, 100)
            : Math.Clamp(completedBeforeCurrent * 100d / total, 0, 100);
        DeploymentProgressText = $"{Math.Max(1, update.StepIndex)}/{total} · {update.Label}";

        if (update.State == DeploymentProgressState.Started)
        {
            _stepStreamedOutput = false;
            if (_liveDeploymentOutput.Length > 0) _liveDeploymentOutput.AppendLine();
            _liveDeploymentOutput.AppendLine($"[{DateTime.Now:HH:mm:ss}] ▶ {update.Label}");
            _liveDeploymentOutput.Append(RunningMarker);
            return;
        }

        RemoveRunningMarker();

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
