using VpsDesk.Domain.Operations;

namespace VpsDesk.Application.Logging;

public static class LogClassifier
{
    public static LogSeverity Classify(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return LogSeverity.Debug;

        if (line.Contains("[OK]", StringComparison.OrdinalIgnoreCase)
            || line.Contains("success", StringComparison.OrdinalIgnoreCase)
            || line.Contains("correctamente", StringComparison.OrdinalIgnoreCase)
            || line.Contains("exit code=0", StringComparison.OrdinalIgnoreCase)
            || line.Contains("exit código=0", StringComparison.OrdinalIgnoreCase))
        {
            return LogSeverity.Success;
        }

        if (line.Contains("ERR:", StringComparison.OrdinalIgnoreCase)
            || line.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Exception", StringComparison.OrdinalIgnoreCase)
            || line.Contains("FATAL", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Unhandled", StringComparison.OrdinalIgnoreCase))
        {
            if (line.Contains("Container", StringComparison.OrdinalIgnoreCase)
                && new[] { "Created", "Creating", "Started", "Running", "Recreated", "Recreate" }
                    .Any(x => line.Contains(x, StringComparison.OrdinalIgnoreCase)))
            {
                return LogSeverity.Warning;
            }

            return LogSeverity.Error;
        }

        return line.Contains("warning", StringComparison.OrdinalIgnoreCase)
            ? LogSeverity.Warning
            : LogSeverity.Info;
    }
}
