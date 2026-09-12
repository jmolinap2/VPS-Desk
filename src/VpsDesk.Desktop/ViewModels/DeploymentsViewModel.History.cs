using System.Collections.ObjectModel;
using System.Text;
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

    public ObservableCollection<OperationHistoryEntry> DeploymentHistory { get; } = new();

    [ObservableProperty] private OperationHistoryEntry? _selectedHistoryEntry;
    [ObservableProperty] private bool _isHistoryView;
    [ObservableProperty] private bool _isPreflightModalOpen;
    [ObservableProperty] private string _preflightModalMessage = string.Empty;
    [ObservableProperty] private bool _isPostflightModalOpen;
    [ObservableProperty] private string _postflightModalMessage = string.Empty;

    public bool IsNewDeploymentView => !IsHistoryView;
    public event EventHandler? HistoryChanged;

    partial void OnIsHistoryViewChanged(bool value)
        => OnPropertyChanged(nameof(IsNewDeploymentView));

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
        }

        if (value.Contains("Verifying the real container", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Verificando", StringComparison.OrdinalIgnoreCase) && value.Contains("contenedor", StringComparison.OrdinalIgnoreCase))
        {
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
    private void ClosePreflightModal()
    {
        if (!IsBusy) IsPreflightModalOpen = false;
    }

    [RelayCommand]
    private void ClosePostflightModal()
    {
        if (!IsBusy) IsPostflightModalOpen = false;
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
        IsPostflightModalOpen = true;
        PostflightModalMessage = "Verificando contenedores y comprobaciones HTTP después del despliegue...";
    }

    private void CompletePostflightModalFromChecks()
    {
        var failed = PostChecks.Any(x => x.Status == PostDeployCheckStatus.Failed);
        var warnings = PostChecks.Any(x => x.Status == PostDeployCheckStatus.Warning);
        PostflightModalMessage = failed
            ? "El postvuelo encontró problemas. El despliegue terminó, pero no debe considerarse saludable todavía."
            : warnings
                ? "Postvuelo completado con advertencias. Revísalas antes de dar por saludable la versión."
                : "Postvuelo correcto. La versión desplegada pasó las comprobaciones.";
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
