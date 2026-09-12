namespace VpsDesk.Domain.Operations;

public enum LogSeverity
{
    Debug,
    Info,
    Success,
    Warning,
    Error
}

public sealed record LogEntry(
    DateTimeOffset Timestamp,
    Guid? ServerId,
    string Source,
    string Message,
    LogSeverity Severity);

public sealed record OperationRun(
    Guid Id,
    Guid ServerId,
    string Operation,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    int? ExitCode,
    bool Cancelled)
{
    public TimeSpan? Duration => EndedAt is null ? null : EndedAt.Value - StartedAt;
    public bool? Succeeded => ExitCode is null ? null : ExitCode == 0 && !Cancelled;
}
