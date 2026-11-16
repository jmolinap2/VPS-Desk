using VpsDesk.Application.Deployments;
using VpsDesk.Desktop.Localization;
using VpsDesk.Desktop.Services;
using VpsDesk.Domain.Activity;

namespace VpsDesk.Desktop.ViewModels;

/// <summary>Informe exportable del pase: de la ejecución actual o de una entrada del historial.</summary>
public partial class DeploymentsViewModel
{
    private DateTimeOffset _lastRunFinishedAt;

    /// <summary>Hay una ejecución terminada (con éxito o no) de la que se puede sacar el informe.</summary>
    public bool CanShareRunReport => !IsDeploymentRunning && !string.IsNullOrWhiteSpace(DeploymentOutput);

    public bool CanShareHistoryReport => SelectedHistoryEntry is not null;

    private void NotifyReportAvailability()
    {
        if (!IsDeploymentRunning) _lastRunFinishedAt = DateTimeOffset.UtcNow;
        OnPropertyChanged(nameof(CanShareRunReport));
    }

    public DeploymentReportData? BuildRunReportData()
    {
        if (!CanShareRunReport) return null;

        var server = _serverAccessor();
        var started = _observedDeploymentStartedAt == default ? DateTimeOffset.UtcNow : _observedDeploymentStartedAt;
        var finished = _lastRunFinishedAt == default ? DateTimeOffset.UtcNow : _lastRunFinishedAt;
        var steps = Steps
            .Select(x => new DeploymentReportStep(x.Label, x.ExitCode, x.Succeeded, x.IsBlocking, x.Duration))
            .ToList();
        var details = string.Join(Environment.NewLine, PostChecks.Select(x => $"[{x.Status}] {x.Label}: {x.Detail}"));
        var title = string.IsNullOrWhiteSpace(DeploymentOutcomeTitle) ? StatusMessage : DeploymentOutcomeTitle;

        return new DeploymentReportData(
            title,
            StatusMessage,
            server?.Name ?? string.Empty,
            server?.Environment.ToString() ?? string.Empty,
            RemoteRepositoryPath.Trim(),
            Branch.Trim(),
            ComposeFile.Trim(),
            started,
            finished < started ? started : finished,
            Steps.FirstOrDefault(x => x.IsBlocking && !x.Succeeded)?.Label,
            steps,
            details,
            DeploymentOutput);
    }

    public DeploymentReportData? BuildHistoryReportData()
    {
        if (SelectedHistoryEntry is not { } entry) return null;
        var label = LocalizationService.T(entry.Outcome switch
        {
            OperationOutcome.Success => "Deployments_OutcomeSuccess",
            OperationOutcome.Warning => "Deployments_OutcomeWarning",
            OperationOutcome.Failed => "Deployments_OutcomeFailed",
            _ => "Deployments_OutcomeUnknown"
        });
        return DeploymentReportData.FromHistory(entry, label);
    }
}
