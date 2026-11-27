using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using VpsDesk.Application.Logging;
using VpsDesk.Desktop.Localization;

namespace VpsDesk.Desktop.Services;

/// <summary>
/// Informe en texto plano del registro de actividad de la aplicación, para pegar en un chat o un ticket
/// o guardar como archivo. Se vuelve a sanear antes de salir de la aplicación.
/// </summary>
public static class ActivityReport
{
    public static string Build(IReadOnlyList<ActivityLogEntry> entries, bool onlyProblems)
    {
        var text = new StringBuilder();
        text.AppendLine($"# {LocalizationService.T("Activity_ReportTitle")}");
        text.AppendLine($"VPS Desk {AppVersion()} · {RuntimeInformation.OSDescription.Trim()} · {DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}");

        var errors = entries.Count(x => x.Level == ActivityLevel.Error);
        var warnings = entries.Count(x => x.Level == ActivityLevel.Warning);
        text.AppendLine(string.Format(CultureInfo.CurrentCulture, LocalizationService.T("Activity_ReportSummary"),
            entries.Count, errors, warnings, ActivityLog.Retention.TotalDays));
        if (onlyProblems) text.AppendLine(LocalizationService.T("Activity_ReportOnlyProblems"));
        text.AppendLine();

        if (entries.Count == 0)
        {
            text.AppendLine(LocalizationService.T("Activity_Empty"));
        }
        else
        {
            foreach (var entry in entries) text.AppendLine(ActivityLog.Format(entry));
        }

        return LogSanitizer.Sanitize(text.ToString().TrimEnd() + Environment.NewLine);
    }

    public static string SuggestFileName() => $"actividad-{DateTime.Now:yyyyMMdd-HHmm}.txt";

    private static string AppVersion()
        => typeof(ActivityReport).Assembly.GetName().Version?.ToString(3) ?? "?";
}
