using System.Diagnostics;
using System.Text;
using Renci.SshNet;
using VpsDesk.Application.Abstractions;

namespace VpsDesk.Infrastructure.Ssh;

public sealed class SshNetCommandExecutor : ISshCommandExecutor
{
    // Tras terminar el comando, tiempo máximo para vaciar los flujos de salida.
    private static readonly TimeSpan PumpDrainTimeout = TimeSpan.FromSeconds(5);

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
            return request.OutputReceived is null
                ? await Task.Run(() => ExecuteCore(request, secret, stopwatch, linkedCts.Token), linkedCts.Token)
                : await Task.Run(() => ExecuteStreamingAsync(request, secret, stopwatch, linkedCts.Token), linkedCts.Token);
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
        var policy = SshConnectionFactory.ApplyHostKeyPolicy(client, request.Server);
        ConnectExplainingHostKey(client, policy);
        cancellationToken.ThrowIfCancellationRequested();

        using var command = client.CreateCommand(NormalizeRemoteCommand(request.Command));
        command.CommandTimeout = request.Timeout;
        var stdout = command.Execute();
        var stderr = command.Error ?? string.Empty;
        var exitCode = command.ExitStatus ?? -1;
        client.Disconnect();

        return new SshCommandResult(exitCode, stdout ?? string.Empty, stderr, stopwatch.Elapsed, false, false);
    }

    /// <summary>
    /// Igual que <see cref="ExecuteCore"/> pero lee stdout/stderr mientras el comando corre y
    /// entrega cada línea completa a <see cref="SshCommandRequest.OutputReceived"/>.
    /// El token cancela el comando remoto (señal) además de la espera local.
    /// </summary>
    private static async Task<SshCommandResult> ExecuteStreamingAsync(
        SshCommandRequest request,
        string? secret,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var client = new SshClient(SshConnectionFactory.Create(request.Server, secret, request.Timeout));
        var policy = SshConnectionFactory.ApplyHostKeyPolicy(client, request.Server);
        ConnectExplainingHostKey(client, policy);
        cancellationToken.ThrowIfCancellationRequested();

        using var command = client.CreateCommand(NormalizeRemoteCommand(request.Command));
        command.CommandTimeout = request.Timeout;

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var onOutput = request.OutputReceived!;

        // Los flujos solo existen una vez iniciado el comando, por eso ExecuteAsync va primero.
        // Los lectores no usan command.Result/Error: esas propiedades leen estos mismos flujos
        // y quedarían vacías si ya los hemos consumido.
        var execution = command.ExecuteAsync(cancellationToken);
        var pumps = Task.WhenAll(
            Task.Run(() => PumpLines(command.OutputStream, stdout, onOutput)),
            Task.Run(() => PumpLines(command.ExtendedOutputStream, stderr, onOutput)));

        try
        {
            await execution.ConfigureAwait(false);
        }
        finally
        {
            // Si el canal no cierra los flujos a tiempo no bloqueamos el despliegue:
            // seguimos con lo acumulado hasta ese momento.
            await Task.WhenAny(pumps, Task.Delay(PumpDrainTimeout)).ConfigureAwait(false);
        }

        var exitCode = command.ExitStatus ?? -1;
        client.Disconnect();

        return new SshCommandResult(
            exitCode,
            Snapshot(stdout),
            Snapshot(stderr),
            stopwatch.Elapsed,
            false,
            false);
    }

    private static void PumpLines(Stream stream, StringBuilder sink, Action<string> onLine)
    {
        try
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
            while (reader.ReadLine() is { } line)
            {
                lock (sink) sink.AppendLine(line);
                try { onLine(line); }
                catch { /* un observador defectuoso no debe romper el despliegue */ }
            }
        }
        catch (ObjectDisposedException)
        {
            // El comando se liberó mientras se leía: no hay más salida.
        }
    }

    private static string Snapshot(StringBuilder sink)
    {
        lock (sink) return sink.ToString();
    }

    // Si la conexion falla porque la huella del servidor no coincide, el mensaje dice cual
    // presento el servidor y cual estaba guardada, en vez de un error generico.
    private static void ConnectExplainingHostKey(SshClient client, HostKeyPolicy policy)
    {
        try
        {
            client.Connect();
        }
        catch (Exception ex) when (policy.Mismatch is not null)
        {
            throw new InvalidOperationException(policy.Mismatch.Describe(), ex);
        }
    }

    private static string NormalizeRemoteCommand(string command)
        => command
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
}
