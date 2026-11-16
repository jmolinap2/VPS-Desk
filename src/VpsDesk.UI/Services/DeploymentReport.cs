using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using VpsDesk.Application.Logging;
using VpsDesk.Desktop.Localization;
using VpsDesk.Domain.Activity;

namespace VpsDesk.Desktop.Services;

public sealed record DeploymentReportStep(string Label, int ExitCode, bool Succeeded, bool IsBlocking, TimeSpan Duration);

/// <summary>Datos de un pase listos para convertirse en texto compartible.</summary>
public sealed record DeploymentReportData(
    string Outcome,
    string Summary,
    string Server,
    string Environment,
    string? Repository,
    string? Branch,
    string? ComposeFile,
    DateTimeOffset Started,
    DateTimeOffset Finished,
    string? FailedStep,
    IReadOnlyList<DeploymentReportStep> Steps,
    string? Details,
    string Output)
{
    public static DeploymentReportData FromHistory(OperationHistoryEntry entry, string outcomeLabel) => new(
        outcomeLabel, entry.Summary, entry.ServerName, entry.Environment, entry.RemoteRepositoryPath, entry.Branch,
        entry.ComposeFile, entry.StartedAt, entry.FinishedAt, entry.FailedStep, [], entry.Details, entry.Output ?? string.Empty);
}

/// <summary>
/// Informe de un pase en texto plano para copiar y pegar en un chat o un ticket. Incluye el resultado,
/// los pasos, la salida completa y, cuando el fallo es reconocible, un diagnóstico probable.
/// La salida se vuelve a sanear antes de salir de la aplicación.
/// </summary>
public static class DeploymentReport
{
    public static string Build(DeploymentReportData data)
    {
        var text = new StringBuilder();
        text.AppendLine($"# {LocalizationService.T("Report_Title")}");
        text.AppendLine($"VPS Desk {AppVersion()} · {DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}");
        text.AppendLine();
        text.AppendLine($"{LocalizationService.T("Report_Result")}: {data.Outcome} — {data.Summary}");
        Line(text, "Deployments_Server", data.Server);
        Line(text, "Deployments_Environment", data.Environment);
        Line(text, "Deployments_Repository", data.Repository);
        Line(text, "Deployments_Branch", data.Branch);
        Line(text, "Deployments_Compose", data.ComposeFile);
        text.AppendLine($"{LocalizationService.T("Report_Started")}: {data.Started.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        text.AppendLine($"{LocalizationService.T("Report_Finished")}: {data.Finished.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        var duration = data.Finished - data.Started;
        if (duration > TimeSpan.Zero)
            text.AppendLine($"{LocalizationService.T("Deployments_Duration")}: {duration.TotalSeconds:F1} s");
        Line(text, "Deployments_FailedStep", data.FailedStep);

        if (data.Steps.Count > 0)
        {
            text.AppendLine();
            text.AppendLine($"## {LocalizationService.T("Deployments_TabSteps")}");
            foreach (var step in data.Steps)
            {
                var marker = step.Succeeded ? "OK " : step.IsBlocking ? "ERR" : "WARN";
                text.AppendLine($"[{marker}] {step.Label} · exit {step.ExitCode} · {step.Duration.TotalSeconds:F1}s");
            }
        }

        if (!string.IsNullOrWhiteSpace(data.Details))
        {
            text.AppendLine();
            text.AppendLine($"## {LocalizationService.T("Deployments_HistoryChecks")}");
            text.AppendLine(data.Details.Trim());
        }

        var hints = Diagnose(data.Output, data.Repository, data.Branch);
        if (hints.Count > 0)
        {
            text.AppendLine();
            text.AppendLine($"## {LocalizationService.T("Report_Diagnosis")}");
            foreach (var hint in hints) text.AppendLine($"- {hint}");
        }

        text.AppendLine();
        text.AppendLine($"## {LocalizationService.T("Report_Output")}");
        text.AppendLine("```");
        text.AppendLine(string.IsNullOrWhiteSpace(data.Output) ? LocalizationService.T("Report_NoOutput") : data.Output.Trim());
        text.AppendLine("```");

        return LogSanitizer.Sanitize(text.ToString().TrimEnd() + Environment.NewLine);
    }

    /// <summary>Nombre de archivo sugerido: pase-AAAAMMDD-HHmm-estado.txt.</summary>
    public static string SuggestFileName(DeploymentReportData data)
    {
        var state = new string(data.Outcome.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        return $"pase-{data.Started.ToLocalTime():yyyyMMdd-HHmm}-{(state.Length == 0 ? "informe" : state)}.txt";
    }

    /// <summary>Causas frecuentes reconocibles en la salida de git/docker, con el siguiente paso sugerido.</summary>
    public static IReadOnlyList<string> Diagnose(string? output, string? repository, string? branch)
    {
        var hints = new List<string>();
        if (string.IsNullOrWhiteSpace(output)) return hints;

        var repo = string.IsNullOrWhiteSpace(repository) ? "<repo>" : repository.Trim();
        var rama = string.IsNullOrWhiteSpace(branch) ? "<rama>" : branch.Trim();

        void Add(string pattern, string key, params object[] args)
        {
            if (Regex.IsMatch(output, pattern, RegexOptions.IgnoreCase))
                hints.Add(LocalizationService.Current.Format(key, args));
        }

        Add(@"not possible to fast-forward|have diverged|diverging branches", "Report_Hint_Diverged", repo, rama);
        Add(@"would be overwritten by (merge|checkout)|your local changes", "Report_Hint_LocalChanges", repo);
        Add(@"could not read from remote repository|could not resolve host|connection timed out|repository not found", "Report_Hint_Remote");
        Add(@"permission denied|publickey", "Report_Hint_Permission");
        Add(@"port is already allocated|address already in use", "Report_Hint_Port");
        Add(@"no space left on device", "Report_Hint_Disk");
        return hints;
    }

    private static void Line(StringBuilder text, string labelKey, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) text.AppendLine($"{LocalizationService.T(labelKey)}: {value.Trim()}");
    }

    private static string AppVersion()
        => typeof(DeploymentReport).Assembly.GetName().Version?.ToString(3) ?? "?";
}
