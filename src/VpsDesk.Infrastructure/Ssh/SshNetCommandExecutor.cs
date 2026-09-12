using System.Diagnostics;
using Renci.SshNet;
using VpsDesk.Application.Abstractions;
using VpsDesk.Domain.Servers;

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
    }

    private static SshCommandResult ExecuteCore(
        SshCommandRequest request,
        string? secret,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var client = new SshClient(CreateConnectionInfo(request.Server, secret, request.Timeout));
        client.Connect();
        cancellationToken.ThrowIfCancellationRequested();

        using var command = client.CreateCommand(request.Command);
        command.CommandTimeout = request.Timeout;
        var stdout = command.Execute();
        var stderr = command.Error ?? string.Empty;
        var exitCode = command.ExitStatus;
        client.Disconnect();

        return new SshCommandResult(exitCode, stdout ?? string.Empty, stderr, stopwatch.Elapsed, false, false);
    }

    private static ConnectionInfo CreateConnectionInfo(ServerProfile server, string? secret, TimeSpan timeout)
    {
        AuthenticationMethod auth = server.AuthenticationType switch
        {
            SshAuthenticationType.PrivateKey when !string.IsNullOrWhiteSpace(server.PrivateKeyPath)
                => new PrivateKeyAuthenticationMethod(
                    server.Username,
                    string.IsNullOrEmpty(secret)
                        ? new PrivateKeyFile(server.PrivateKeyPath)
                        : new PrivateKeyFile(server.PrivateKeyPath, secret)),
            SshAuthenticationType.Password when !string.IsNullOrEmpty(secret)
                => new PasswordAuthenticationMethod(server.Username, secret),
            _ => throw new InvalidOperationException("The configured SSH authentication method is not ready.")
        };

        return new ConnectionInfo(server.Host, server.Port, server.Username, auth)
        {
            Timeout = timeout
        };
    }
}
