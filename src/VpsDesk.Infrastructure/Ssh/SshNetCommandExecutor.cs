using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;
using VpsDesk.Application.Abstractions;
using VpsDesk.Application.Logging;

namespace VpsDesk.Infrastructure.Ssh;

public sealed class SshNetCommandExecutor : ISshCommandExecutor
{
    // Tras terminar el comando, tiempo máximo para vaciar los flujos de salida.
    private static readonly TimeSpan PumpDrainTimeout = TimeSpan.FromSeconds(5);

    // Margen sobre el plazo del comando para la ruta síncrona (conectar + ejecutar).
    private static readonly TimeSpan SyncGrace = TimeSpan.FromSeconds(10);

    public async Task<SshCommandResult> ExecuteAsync(
        SshCommandRequest request,
        string? secret,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteCoreAsync(request, secret, cancellationToken);
        // Solo los comandos que no terminaron bien: los correctos son demasiados y no explican un fallo.
        // Nunca se registra el texto del comando ni su salida, solo cómo terminó.
        if (!result.Succeeded)
        {
            var level = result.Cancelled ? ActivityLevel.Warning : ActivityLevel.Error;
            ActivityLog.Record(level, "ssh", Describe(result), request.Server.Name, result.Duration);
        }

        return result;
    }

    private static string Describe(SshCommandResult result)
    {
        if (result.Cancelled) return "comando cancelado";
        if (result.TimedOut) return "plazo agotado";
        var detail = (result.StandardError ?? string.Empty).Trim().Split('\n')[0];
        var reason = result.ConnectionLost ? "conexión perdida" : $"salió con código {result.ExitCode}";
        return detail.Length == 0 ? reason : $"{reason}: {detail}";
    }

    private async Task<SshCommandResult> ExecuteCoreAsync(
        SshCommandRequest request,
        string? secret,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(request.Timeout);

        try
        {
            if (request.OutputReceived is null && request.OutputChunkReceived is null)
            {
                // ExecuteCore es síncrono (Connect/Execute bloquean el hilo): el token solo se mira
                // entre pasos y no interrumpe una conexión colgada. WaitAsync deja de esperar al
                // cancelar el usuario o al vencer el plazo (con un margen para conectar + ejecutar),
                // así la interfaz nunca queda atada a un socket que no responde. El hilo bloqueado
                // termina solo cuando SSH.NET agota su propio tiempo.
                return await Task.Run(() => ExecuteCore(request, secret, stopwatch, linkedCts.Token), linkedCts.Token)
                    .WaitAsync(request.Timeout + SyncGrace, cancellationToken);
            }

            return await Task.Run(() => ExecuteStreamingAsync(request, secret, stopwatch, linkedCts.Token), linkedCts.Token);
        }
        catch (TimeoutException)
        {
            return new SshCommandResult(-1, string.Empty, "SSH command timed out.", stopwatch.Elapsed, true, false);
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
            return new SshCommandResult(-1, string.Empty, ex.Message, stopwatch.Elapsed, false, false,
                ConnectionLost: IsConnectionFailure(ex));
        }
    }

    private static bool IsConnectionFailure(Exception exception)
    {
        // La autenticación y las discrepancias de huella no son cortes transitorios.
        if (exception is Renci.SshNet.Common.SshAuthenticationException or InvalidOperationException)
            return false;
        return exception is System.Net.Sockets.SocketException or System.IO.IOException
            or Renci.SshNet.Common.SshConnectionException
            || exception.InnerException is { } inner && IsConnectionFailure(inner);
    }

    // Paquetes de unos pocos bytes: evitan que un NAT de red móvil cierre un paso largo sin salida.
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(30);

    private static SshCommandResult ExecuteCore(
        SshCommandRequest request,
        string? secret,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var client = ConnectWithRetry(request, secret, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        using var command = client.CreateCommand(NormalizeRemoteCommand(request.Command));
        command.CommandTimeout = request.Timeout;
        var stdout = command.Execute();
        var stderr = command.Error ?? string.Empty;
        var exitCode = command.ExitStatus ?? -1;
        // El resultado ya llegó. Dispose cierra la sesión; no enviar otro mensaje
        // de desconexión que pueda convertir un comando exitoso en un fallo de red.

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

        using var client = ConnectWithRetry(request, secret, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Un solo canal conserva el orden de stdout/stderr tal como lo escribe el proceso.
        var commandText = NormalizeRemoteCommand(request.Command);
        if (request.OutputChunkReceived is not null) commandText = "{\n" + commandText + "\n} 2>&1";
        using var command = client.CreateCommand(commandText);
        command.CommandTimeout = request.Timeout;

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var onOutput = request.OutputReceived!;
        var outputGate = new object();
        var acceptChunks = true;

        // Los flujos solo existen una vez iniciado el comando, por eso ExecuteAsync va primero.
        // Los lectores no usan command.Result/Error: esas propiedades leen estos mismos flujos
        // y quedarían vacías si ya los hemos consumido.
        var execution = command.ExecuteAsync(cancellationToken);
        var pumps = request.OutputChunkReceived is { } onChunk
            ? Task.Run(() => PumpChunks(command.OutputStream, stdout, (text, completed) =>
            {
                lock (outputGate)
                {
                    if (acceptChunks) onChunk(text, completed);
                }
            }))
            : Task.WhenAll(
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
            // Ninguna lectura tardía puede modificar el paso siguiente ni su registro.
            lock (outputGate) acceptChunks = false;
        }

        var exitCode = command.ExitStatus ?? -1;

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

    private static void PumpChunks(Stream stream, StringBuilder sink, Action<string, bool> onChunk)
    {
        try
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);
            var buffer = new char[4096];
            int count;
            while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                var text = new string(buffer, 0, count);
                lock (sink) sink.Append(text);
                onChunk(text, false);
            }
            onChunk(string.Empty, true);
        }
        catch (ObjectDisposedException) { }
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

    // Reintentos SOLO de la fase de conexión: todavía no se ha enviado ningún comando, así que
    // repetirla nunca puede aplicar nada dos veces. Un corte transitorio del transporte (cambio de
    // red, "Software caused connection abort", reinicio de la pila de red del teléfono) no debe
    // tirar un pase. Una conexión que no contesta también se reintenta, pero cada intento usa solo una
    // parte del plazo del paso (ConnectAttemptTimeout), así el total no se alarga. Las credenciales
    // rechazadas y la huella distinta NO se reintentan: repetirlas solo cargaría al servidor.
    private static readonly TimeSpan[] ConnectRetryDelays = [TimeSpan.FromMilliseconds(800), TimeSpan.FromMilliseconds(2500)];

    // Cada intento de conexión espera una parte del plazo del paso, no todo: así un servidor que acepta el TCP y no
    // contesta (red móvil inestable, sshd saturado) se reintenta en vez de consumir el plazo entero en un único intento.
    private static readonly TimeSpan MinConnectAttempt = TimeSpan.FromSeconds(8);

    private static TimeSpan ConnectAttemptTimeout(TimeSpan total)
    {
        var share = TimeSpan.FromTicks(total.Ticks / (ConnectRetryDelays.Length + 1));
        var attempt = share > MinConnectAttempt ? share : MinConnectAttempt;
        return attempt < total ? attempt : total;
    }

    private static SshClient ConnectWithRetry(SshCommandRequest request, string? secret, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var client = new SshClient(SshConnectionFactory.Create(request.Server, secret, ConnectAttemptTimeout(request.Timeout)));
            var connecting = Stopwatch.StartNew();
            try
            {
                client.KeepAliveInterval = KeepAliveInterval;
                var policy = SshConnectionFactory.ApplyHostKeyPolicy(client, request.Server);
                ConnectExplainingHostKey(client, policy);
                // Una conexión lenta o que costó reintentos es justo lo que hay que poder ver después.
                if (attempt > 0 || connecting.Elapsed > TimeSpan.FromSeconds(3))
                    ActivityLog.Record(ActivityLevel.Info, "ssh",
                        attempt > 0 ? $"conectó en el intento {attempt + 1}" : "conexión lenta", request.Server.Name, connecting.Elapsed);
                return client;
            }
            catch (Exception ex) when (attempt < ConnectRetryDelays.Length && IsTransientConnectFailure(ex))
            {
                ActivityLog.Record(ActivityLevel.Warning, "ssh",
                    $"conexión fallida, reintento {attempt + 1} de {ConnectRetryDelays.Length}: {ex.Message}", request.Server.Name);
                client.Dispose();
                // Espera que se corta al cancelar, en vez de un Sleep ciego.
                if (cancellationToken.WaitHandle.WaitOne(ConnectRetryDelays[attempt]))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
    }

    private static bool IsTransientConnectFailure(Exception ex)
        => ex switch
        {
            SshAuthenticationException => false,
            // Solo se evalúa al conectar: si venció la espera, el comando todavía no se había enviado, así que repetir es seguro.
            SshOperationTimeoutException => true,
            TimeoutException => false,
            OperationCanceledException => false,
            InvalidOperationException => false,   // huella distinta (ConnectExplainingHostKey) u otro error lógico
            SocketException => true,
            SshConnectionException => true,
            System.IO.IOException => true,
            _ => ex.InnerException is not null && IsTransientConnectFailure(ex.InnerException)
        };

    private static string NormalizeRemoteCommand(string command)
        => command
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
}
