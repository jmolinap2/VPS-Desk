using System.Diagnostics;
using System.Text;
using VpsDesk.Application.Abstractions;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Application.Deployments;

public sealed record DetachedJobOptions(
    TimeSpan PollInterval,
    TimeSpan ReconnectBudget,
    TimeSpan MaxBackoff,
    int MaxCapturedBytes)
{
    /// <summary>Consulta cada 1,5 s; espera hasta 10 minutos sin conexión antes de declarar el estado sin confirmar.</summary>
    public static DetachedJobOptions Default { get; } = new(
        TimeSpan.FromMilliseconds(1500), TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(15), 4 * 1024 * 1024);
}

/// <param name="Unconfirmed">
/// No se pudo volver a consultar el trabajo dentro del plazo. El trabajo **sigue o terminó en el servidor**: no es un fallo
/// confirmado. Con <see cref="JobId"/> se puede volver a consultar con <see cref="DetachedRemoteJob.AttachAsync"/>.
/// </param>
public sealed record DetachedJobResult(
    string JobId,
    int ExitCode,
    string Output,
    TimeSpan Duration,
    bool TimedOut,
    bool Cancelled,
    bool Unconfirmed)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut && !Cancelled && !Unconfirmed;
}

/// <summary>
/// Ejecuta un comando largo **desacoplado de la sesión SSH**: el servidor lo corre con <c>setsid nohup</c>, escribe su salida y su
/// código de salida en archivos, y el cliente consulta el avance con conexiones cortas e independientes. Si la conexión se
/// corta, el comando sigue en el VPS y el cliente espera y vuelve a consultar, sin repetir el comando. Así un corte de internet
/// no deja un <c>docker compose up</c> a medias ni obliga a empezar de nuevo.
///
/// Archivos del trabajo, en <c>$HOME/.vpsdesk/jobs/&lt;id&gt;/</c>: <c>cmd.sh</c> (el comando), <c>run.sh</c> (lanzador),
/// <c>out</c> (salida combinada), <c>pid</c> (grupo de procesos) y <c>exit</c> (código de salida, solo al terminar).
/// El inicio es idempotente por id: repetir el inicio tras un corte no lanza el comando dos veces.
/// </summary>
public sealed class DetachedRemoteJob
{
    private const string StateMarker = "@@VPSDESK-STATE|";
    private const string StartedMarker = "@@VPSDESK-STARTED";

    private const string RunScript =
        "#!/bin/sh\n" +
        "echo $$ > \"$1/pid\"\n" +
        "sh \"$1/cmd.sh\" > \"$1/out\" 2>&1 < /dev/null\n" +
        "rc=$?\n" +
        "echo \"$rc\" > \"$1/exit.tmp\"\n" +
        "mv \"$1/exit.tmp\" \"$1/exit\"\n";

    private readonly ISshCommandExecutor _ssh;
    private readonly DetachedJobOptions _options;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public DetachedRemoteJob(
        ISshCommandExecutor ssh,
        DetachedJobOptions? options = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _ssh = ssh;
        _options = options ?? DetachedJobOptions.Default;
        _delay = delay ?? Task.Delay;
    }

    public static string NewJobId() => "vd" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss") + Guid.NewGuid().ToString("N")[..8];

    /// <summary>Lanza el comando en el servidor y espera su resultado consultando su avance.</summary>
    public async Task<DetachedJobResult> RunAsync(
        ServerProfile server, string? secret, string jobId, string command, TimeSpan timeout,
        Action<string, bool>? onChunk, Action<string>? onStatus, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        if (!await StartAsync(server, secret, jobId, command, onStatus, cancellationToken))
        {
            return new DetachedJobResult(jobId, -1, "[No se pudo iniciar el paso en el servidor.]", clock.Elapsed, false, false, true);
        }
        return await PollAsync(server, secret, jobId, timeout, clock, onChunk, onStatus, cancellationToken);
    }

    /// <summary>Vuelve a consultar un trabajo ya iniciado (por ejemplo, tras reabrir la aplicación). Lee la salida desde el principio.</summary>
    public Task<DetachedJobResult> AttachAsync(
        ServerProfile server, string? secret, string jobId, TimeSpan timeout,
        Action<string, bool>? onChunk, Action<string>? onStatus, CancellationToken cancellationToken)
        => PollAsync(server, secret, jobId, timeout, Stopwatch.StartNew(), onChunk, onStatus, cancellationToken);

    private async Task<bool> StartAsync(
        ServerProfile server, string? secret, string jobId, string command, Action<string>? onStatus, CancellationToken ct)
    {
        var script = BuildStartScript(jobId, command);
        var request = new SshCommandRequest(server, script, TimeSpan.FromSeconds(30));
        // El inicio es idempotente por id: si la respuesta se perdió pero el servidor ya lo lanzó, repetirlo no lo lanza otra vez.
        var delays = new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(15) };
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var result = await _ssh.ExecuteAsync(request, secret, ct);
            if (result.StandardOutput.Contains(StartedMarker, StringComparison.Ordinal)) return true;
            if (!result.ConnectionLost && !result.TimedOut) return false; // rechazo lógico (autenticación, permisos): no es un corte
            if (attempt >= delays.Length) return false;
            onStatus?.Invoke($"Sin conexión al iniciar el paso. Reintentando ({attempt + 1} de {delays.Length})...");
            await _delay(delays[attempt], ct);
        }
    }

    private async Task<DetachedJobResult> PollAsync(
        ServerProfile server, string? secret, string jobId, TimeSpan timeout, Stopwatch clock,
        Action<string, bool>? onChunk, Action<string>? onStatus, CancellationToken ct)
    {
        var output = new StringBuilder();
        long offset = 0;
        DateTimeOffset? lostSince = null;
        var backoff = TimeSpan.FromSeconds(2);
        var killed = false;
        DateTimeOffset killedAt = default;

        while (true)
        {
            if (ct.IsCancellationRequested)
            {
                await KillAsync(server, secret, jobId);
                onChunk?.Invoke(string.Empty, true);
                return new DetachedJobResult(jobId, -1, output.ToString(), clock.Elapsed, false, true, false);
            }

            if (!killed && clock.Elapsed > timeout)
            {
                killed = true;
                killedAt = DateTimeOffset.UtcNow;
                onStatus?.Invoke("El paso superó su plazo. Deteniéndolo en el servidor...");
                await KillAsync(server, secret, jobId);
            }

            var pollStarted = DateTimeOffset.UtcNow;
            var poll = await _ssh.ExecuteAsync(
                new SshCommandRequest(server, BuildPollScript(jobId, offset), TimeSpan.FromSeconds(20)), secret, CancellationToken.None);
            var parsed = Parse(poll.StandardOutput);

            if (parsed is null)
            {
                // Sin conexión (o respuesta ilegible): el paso sigue en el servidor. Se espera y se vuelve a consultar.
                lostSince ??= pollStarted;
                var offline = DateTimeOffset.UtcNow - lostSince.Value;
                if (offline > _options.ReconnectBudget || IsLogicalRejection(poll))
                {
                    onChunk?.Invoke(string.Empty, true);
                    output.Append($"\n[Sin conexión con el servidor. El paso sigue o terminó en el VPS (trabajo {jobId}); su estado no está confirmado.]\n");
                    return new DetachedJobResult(jobId, -1, output.ToString(), clock.Elapsed, killed, false, true);
                }
                onStatus?.Invoke($"Sin conexión con el servidor; el paso sigue en el VPS. Reintentando en {backoff.TotalSeconds:0} s...");
                try { await _delay(backoff, ct); }
                catch (OperationCanceledException) { continue; }
                backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, _options.MaxBackoff.TotalSeconds));
                continue;
            }

            if (lostSince is not null)
            {
                onStatus?.Invoke("Conexión recuperada. El paso continúa.");
                lostSince = null;
                backoff = TimeSpan.FromSeconds(2);
            }

            if (parsed.Missing)
            {
                return new DetachedJobResult(jobId, -1, output.ToString() + "\n[El trabajo ya no existe en el servidor.]\n", clock.Elapsed, false, false, true);
            }

            if (parsed.Chunk.Length > 0)
            {
                Append(output, parsed.Chunk);
                onChunk?.Invoke(parsed.Chunk, false);
            }
            offset = parsed.Size;

            if (parsed.ExitCode is { } exit)
            {
                onChunk?.Invoke(string.Empty, true);
                await CleanupAsync(server, secret, jobId);
                return new DetachedJobResult(jobId, killed ? -1 : exit, output.ToString(), clock.Elapsed, killed, false, false);
            }

            if (killed && DateTimeOffset.UtcNow - killedAt > TimeSpan.FromSeconds(20))
            {
                // Se pidió detenerlo y sigue vivo: no esperar para siempre.
                onChunk?.Invoke(string.Empty, true);
                output.Append($"\n[El paso superó su plazo y no se pudo confirmar que se detuviera en el servidor (trabajo {jobId}).]\n");
                return new DetachedJobResult(jobId, -1, output.ToString(), clock.Elapsed, true, false, false);
            }

            try { await _delay(_options.PollInterval, ct); }
            catch (OperationCanceledException) { /* el ciclo detecta la cancelación y detiene el paso en el servidor */ }
        }
    }

    /// <summary>
    /// Un rechazo que esperar no arregla (credenciales, huella del servidor): se informa de inmediato. Todo lo demás (conexión
    /// cerrada, tiempo agotado, lectura sin respuesta) es la red y se espera hasta agotar el plazo sin conexión.
    /// </summary>
    private static bool IsLogicalRejection(SshCommandResult poll)
        => !poll.ConnectionLost && !poll.TimedOut
           && System.Text.RegularExpressions.Regex.IsMatch(poll.StandardError ?? string.Empty,
               "authenticat|permission denied|host key|fingerprint|huella|denied", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private void Append(StringBuilder output, string chunk)
    {
        output.Append(chunk);
        if (output.Length > _options.MaxCapturedBytes) output.Remove(0, output.Length - _options.MaxCapturedBytes);
    }

    private async Task KillAsync(ServerProfile server, string? secret, string jobId)
    {
        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await _ssh.ExecuteAsync(new SshCommandRequest(server, BuildKillScript(jobId), TimeSpan.FromSeconds(15)), secret, limit.Token);
        }
        catch { /* mejor esfuerzo: si no hay conexión, el trabajo queda y el usuario lo ve en la verificación */ }
    }

    private async Task CleanupAsync(ServerProfile server, string? secret, string jobId)
    {
        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _ssh.ExecuteAsync(new SshCommandRequest(server, $"rm -rf \"$HOME/.vpsdesk/jobs/{jobId}\"", TimeSpan.FromSeconds(10)), secret, limit.Token);
        }
        catch { /* los archivos del trabajo son pequeños; no impiden nada */ }
    }

    // ───── Scripts remotos (sh POSIX) ─────

    /// <summary>Script de inicio; público para que las pruebas comprueben que repetirlo no lanza el comando dos veces.</summary>
    public static string BuildStartScript(string jobId, string command)
    {
        var cmd = Convert.ToBase64String(Encoding.UTF8.GetBytes(command));
        var run = Convert.ToBase64String(Encoding.UTF8.GetBytes(RunScript));
        return
            $"d=\"$HOME/.vpsdesk/jobs/{jobId}\"\n" +
            "mkdir -p \"$HOME/.vpsdesk/jobs\" || exit 1\n" +
            "if mkdir \"$d\" 2>/dev/null; then\n" +
            $"  printf '%s' '{cmd}' | base64 -d > \"$d/cmd.sh\" || exit 1\n" +
            $"  printf '%s' '{run}' | base64 -d > \"$d/run.sh\" || exit 1\n" +
            "  if command -v setsid >/dev/null 2>&1; then\n" +
            "    nohup setsid sh \"$d/run.sh\" \"$d\" >/dev/null 2>&1 < /dev/null &\n" +
            "  else\n" +
            "    nohup sh \"$d/run.sh\" \"$d\" >/dev/null 2>&1 < /dev/null &\n" +
            "  fi\n" +
            "fi\n" +
            $"echo '{StartedMarker}'\n";
    }

    internal static string BuildPollScript(string jobId, long offset)
        => $"d=\"$HOME/.vpsdesk/jobs/{jobId}\"\n" +
           $"if [ ! -d \"$d\" ]; then printf '\\n{StateMarker}missing\\n'; exit 0; fi\n" +
           "if [ -f \"$d/exit\" ]; then ex=$(cat \"$d/exit\"); else ex=running; fi\n" +
           "size=$(wc -c < \"$d/out\" 2>/dev/null || echo 0); size=$(echo $size)\n" +
           $"off={offset}\n" +
           "if [ \"$size\" -gt \"$off\" ]; then tail -c +$((off+1)) \"$d/out\" | head -c $((size-off)); fi\n" +
           $"printf '\\n{StateMarker}%s|%s\\n' \"$size\" \"$ex\"\n";

    internal static string BuildKillScript(string jobId)
        => $"d=\"$HOME/.vpsdesk/jobs/{jobId}\"\n" +
           "if [ -f \"$d/pid\" ] && [ ! -f \"$d/exit\" ]; then p=$(cat \"$d/pid\"); kill -TERM \"-$p\" 2>/dev/null || kill -TERM \"$p\" 2>/dev/null; sleep 2; kill -KILL \"-$p\" 2>/dev/null || true; fi\n";

    // ───── Respuesta de la consulta ─────

    private sealed record Poll(string Chunk, long Size, int? ExitCode, bool Missing);

    private static Poll? Parse(string stdout)
    {
        var index = stdout.LastIndexOf(StateMarker, StringComparison.Ordinal);
        if (index < 0) return null;
        var chunk = stdout[..index];
        if (chunk.EndsWith('\n')) chunk = chunk[..^1]; // el salto que antepone el printf del marcador
        var state = stdout[(index + StateMarker.Length)..].Trim();
        if (state == "missing") return new Poll(string.Empty, 0, null, true);
        var parts = state.Split('|');
        if (parts.Length != 2 || !long.TryParse(parts[0], out var size)) return null;
        int? exit = parts[1] == "running" ? null : int.TryParse(parts[1], out var code) ? code : null;
        if (parts[1] != "running" && exit is null) return null;
        return new Poll(chunk, size, exit, false);
    }
}
