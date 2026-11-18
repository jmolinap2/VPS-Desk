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

    // «Qué falló» del pase abierto en el historial: causa probable y líneas de error de la salida.
    public string HistoryDiagnosis => SelectedHistoryEntry is { } entry
        ? string.Join(Environment.NewLine + Environment.NewLine,
            DeploymentReport.Diagnose(entry.Output, entry.RemoteRepositoryPath, entry.Branch))
        : string.Empty;

    public string HistoryErrorExcerpt => DeploymentReport.ErrorExcerpt(SelectedHistoryEntry?.Output);

    public bool HasHistoryDiagnosis => HistoryDiagnosis.Length > 0;

    public bool HasHistoryFailureInfo => SelectedHistoryEntry is { Outcome: not OperationOutcome.Success } entry
        && (!string.IsNullOrWhiteSpace(entry.FailedStep) || HasHistoryDiagnosis || HistoryErrorExcerpt.Length > 0);

    private void NotifyHistoryFailureInfo()
    {
        OnPropertyChanged(nameof(HistoryDiagnosis));
        OnPropertyChanged(nameof(HistoryErrorExcerpt));
        OnPropertyChanged(nameof(HasHistoryDiagnosis));
        OnPropertyChanged(nameof(HasHistoryFailureInfo));
    }

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
