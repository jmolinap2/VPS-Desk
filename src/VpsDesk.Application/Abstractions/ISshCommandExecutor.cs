using VpsDesk.Domain.Servers;

namespace VpsDesk.Application.Abstractions;

public sealed record SshCommandRequest(
    ServerProfile Server,
    string Command,
    TimeSpan Timeout);

public sealed record SshCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration,
    bool TimedOut,
    bool Cancelled)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut && !Cancelled;
}

public interface ISshCommandExecutor
{
    Task<SshCommandResult> ExecuteAsync(
        SshCommandRequest request,
        string? secret,
        CancellationToken cancellationToken = default);
}
