using System.Diagnostics;
using Renci.SshNet;
using VpsDesk.Application.Abstractions;

namespace VpsDesk.Infrastructure.Ssh;

public sealed class SshNetCommandExecutor : ISshCommandExecutor
{
    public async Task<SshCommandResult> ExecuteAsync(
        SshCommandRequest request,
        string? secret,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(request.Timeout);

        try
        {
            return await Task.Run(() => ExecuteCore(request, secret, stopwatch, linkedCts.Token), linkedCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SshCommandResult(-1, string.Empty, "SSH command timed out.", stopwatch.Elapsed, true, false);
        }
        catch (OperationCanceledException)
        {
            return new SshCommandResult(-1, string.Empty, "SSH command cancelled.", stopwatch.Elapsed, false, true);
        }
        catch (Exception ex)
        {
            return new SshCommandResult(-1, string.Empty, ex.Message, stopwatch.Elapsed, false, false);
        }
    }

    private static SshCommandResult ExecuteCore(
        SshCommandRequest request,
        string? secret,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var client = new SshClient(SshConnectionFactory.Create(request.Server, secret, request.Timeout));
        client.Connect();
        cancellationToken.ThrowIfCancellationRequested();

        using var command = client.CreateCommand(request.Command);
        command.CommandTimeout = request.Timeout;
        var stdout = command.Execute();
        var stderr = command.Error ?? string.Empty;
        var exitCode = command.ExitStatus ?? -1;
        client.Disconnect();

        return new SshCommandResult(exitCode, stdout ?? string.Empty, stderr, stopwatch.Elapsed, false, false);
    }
}
