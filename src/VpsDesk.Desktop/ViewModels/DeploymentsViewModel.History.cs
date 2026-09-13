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

    public ObservableCollection<OperationHistoryEntry> DeploymentHistory { get; } = new();

    [ObservableProperty] private OperationHistoryEntry? _selectedHistoryEntry;
    [ObservableProperty] private bool _isHistoryView;
    [ObservableProperty] private bool _isPreflightModalOpen;
    [ObservableProperty] private string _preflightModalMessage = string.Empty;
    [ObservableProperty] private bool _isPostflightModalOpen;
    [ObservableProperty] private string _postflightModalMessage = string.Empty;
    [ObservableProperty] private int _postflightPassedCount;
    [ObservableProperty] private int _postflightWarningCount;
    [ObservableProperty] private int _postflightFailedCount;
    [ObservableProperty] private bool _isDeploymentRunning;
    [ObservableProperty] private bool _isDeploymentConfigurationExpanded = true;

    public bool IsNewDeploymentView => !IsHistoryView;
    public bool IsDeploymentConfigurationCollapsed => !IsDeploymentConfigurationExpanded;
    public event EventHandler? HistoryChanged;

    partial void OnIsHistoryViewChanged(bool value)
        => OnPropertyChanged(nameof(IsNewDeploymentView));

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
            _liveDeploymentOutput.Clear();
            DeploymentOutput = $"[{DateTime.Now:HH:mm:ss}] Iniciando despliegue...";
            _deployment.ProgressChanged -= OnDeploymentProgressChanged;
            _deployment.ProgressChanged += OnDeploymentProgressChanged;
        }

        if (value.Contains("Verifying the real container", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Verificando", StringComparison.OrdinalIgnoreCase) && value.Contains("contenedor", StringComparison.OrdinalIgnoreCase))
        {
            IsDeploymentRunning = false;
            _deployment.ProgressChanged -= OnDeploymentProgressChanged;
            BeginPostflightModal();
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
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
    private void ShowNewDeployment() => IsHistoryView = false;

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
        if (!IsBusy) IsPreflightModalOpen = false;
    }

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
        IsPreflightModalOpen = true;
        PreflightModalMessage = "Comprobando SSH, Git, Docker, Compose, rama, archivos y entorno...";
    }

    private async Task CompletePreflightModalAsync(bool passed)
    {
        PreflightModalMessage = passed
            ? "Prevuelo correcto. Todo está listo para continuar."
            : "El prevuelo encontró bloqueos. Revisa los elementos marcados antes de desplegar.";

        if (passed)
        {
            await Task.Delay(650);
            IsPreflightModalOpen = false;
        }
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

    private void OnDeploymentProgressChanged(DeploymentProgressUpdate update)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (update.State == DeploymentProgressState.Started)
            {
                if (_liveDeploymentOutput.Length > 0) _liveDeploymentOutput.AppendLine();
                _liveDeploymentOutput.AppendLine($"[{DateTime.Now:HH:mm:ss}] ▶ {update.Label}");
                _liveDeploymentOutput.Append("Ejecutando...");
            }
            else
            {
                if (_liveDeploymentOutput.Length > 0)
                {
                    var text = _liveDeploymentOutput.ToString();
                    if (text.EndsWith("Ejecutando...", StringComparison.Ordinal))
                    {
                        _liveDeploymentOutput.Length -= "Ejecutando...".Length;
                    }
                }

                var marker = update.Succeeded == true ? "✓" : "✕";
                var duration = update.Duration?.TotalSeconds ?? 0;
                _liveDeploymentOutput.AppendLine($"{marker} exit {update.ExitCode ?? -1} · {duration:F1}s");
                if (!string.IsNullOrWhiteSpace(update.Output))
                {
                    _liveDeploymentOutput.AppendLine(update.Output.TrimEnd());
                }
            }

            DeploymentOutput = _liveDeploymentOutput.ToString().TrimEnd();
        });
    }

    private async Task RecordObservedDeploymentAsync()
    {
        if (_historyStore is null) return;
        var server = _serverAccessor();
        if (server is null) return;

        var hasFailedStep = Steps.Any(x => !x.Succeeded);
        var hasFailedPostCheck = PostChecks.Any(x => x.Status == PostDeployCheckStatus.Failed);
        var hasWarning = PostChecks.Any(x => x.Status == PostDeployCheckStatus.Warning);
        var statusLooksFailed = StatusMessage.Contains("failed", StringComparison.OrdinalIgnoreCase)
                                || StatusMessage.Contains("fall", StringComparison.OrdinalIgnoreCase)
                                || StatusMessage.Contains("problemas", StringComparison.OrdinalIgnoreCase);

        var outcome = hasFailedStep || hasFailedPostCheck || statusLooksFailed
            ? OperationOutcome.Failed
            : hasWarning
                ? OperationOutcome.Warning
                : OperationOutcome.Success;

        var failedStep = Steps.FirstOrDefault(x => !x.Succeeded)?.Label;
        var finishedAt = DateTimeOffset.UtcNow;
        var startedAt = _observedDeploymentStartedAt == default ? finishedAt : _observedDeploymentStartedAt;
        var details = new StringBuilder();
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
        _deploymentObserved = false;
    }
}
