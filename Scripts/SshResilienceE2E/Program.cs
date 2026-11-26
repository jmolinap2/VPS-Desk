// E2E de resiliencia de red, contra un servidor SSH REAL (sshd en Docker, solo local) y con cortes de red REALES: un proxy TCP
// entre la aplicación y el servidor se corta y se restaura mientras corren los pasos. Equivale a que se caiga el internet.
//   dotnet run --project Scripts/SshResilienceE2E
// Requiere Docker. Crea y elimina el contenedor «vpsdesk-e2e-sshd»; no toca ningún VPS real.
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using VpsDesk.Application.Abstractions;
using VpsDesk.Application.Deployments;
using VpsDesk.Domain.Servers;
using VpsDesk.Infrastructure.Ssh;

const string Container = "vpsdesk-e2e-sshd";
const int SshPort = 2230;   // puerto del sshd en el equipo
const int ProxyPort = 2231; // puerto que usa la aplicación (el que se corta)

var failures = 0;
void Check(bool passed, string label, string? detail = null)
{
    Console.WriteLine((passed ? "PASS " : "FAIL ") + label + (passed || detail is null ? "" : " -> " + detail));
    if (!passed) failures++;
}

(int Exit, string Output) Run(string file, string args)
{
    var info = new ProcessStartInfo(file, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    using var process = Process.Start(info)!;
    var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
    process.WaitForExit();
    return (process.ExitCode, output);
}
string InContainer(string script, string user = "deploy") => Run("docker", $"exec -u {user} {Container} sh -c \"{script.Replace("\"", "\\\"")}\"").Output.Trim();

var root = AppContext.BaseDirectory;
while (root is not null && !File.Exists(Path.Combine(root, "VpsDesk.slnx"))) root = Path.GetDirectoryName(root);
var dockerDir = Path.Combine(root!, "Scripts", "SshResilienceE2E", "docker");

Console.WriteLine("Preparando el servidor de prueba (Docker)...");
Run("docker", $"rm -f {Container}");
var build = Run("docker", $"build -q -t vpsdesk-e2e-sshd \"{dockerDir}\"");
if (build.Exit != 0) { Console.WriteLine(build.Output); return 1; }
var start = Run("docker", $"run -d --name {Container} -p 127.0.0.1:{SshPort}:22 vpsdesk-e2e-sshd");
if (start.Exit != 0) { Console.WriteLine(start.Output); return 1; }

var proxy = new ToxicProxy(ProxyPort, SshPort);
try
{
    for (var i = 0; i < 40; i++)
    {
        try { using var probe = new TcpClient(); await probe.ConnectAsync("127.0.0.1", SshPort); break; } catch { await Task.Delay(500); }
    }
    proxy.Start();

    var server = new ServerProfile(Guid.NewGuid(), "e2e", "127.0.0.1", ProxyPort, "deploy", SshAuthenticationType.Password,
        null, null, null, ServerEnvironment.Development, []);
    const string Secret = "deploy-pass";
    ISshCommandExecutor ssh = Environment.GetEnvironmentVariable("E2E_TRACE") == "1" ? new TracingSsh(new SshNetCommandExecutor()) : new SshNetCommandExecutor();
    var only = (Environment.GetEnvironmentVariable("E2E_ONLY") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
    bool On(string n) => only.Length == 0 || only.Contains(n);
    var fast = new DetachedJobOptions(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(3), 4 * 1024 * 1024);

    var hello = await ssh.ExecuteAsync(new SshCommandRequest(server, "echo hola", TimeSpan.FromSeconds(10)), Secret);
    Check(hello.Succeeded && hello.StandardOutput.Contains("hola"), "El servidor de prueba responde por el proxy");

    static void CutAfter(ToxicProxy p, int startAfterMs, int downMs, bool blackhole = false) => _ = Task.Run(async () =>
    {
        await Task.Delay(startAfterMs); if (blackhole) p.Blackhole(); else p.Cut(); await Task.Delay(downMs); p.Restore();
    });
    static string[] Lines(string text) => text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
    var expected20 = Enumerable.Range(1, 20).Select(i => $"line {i}").ToArray();

    // ── 1. Camino feliz ──
    var id = DetachedRemoteJob.NewJobId();
    var ok = await new DetachedRemoteJob(ssh, fast).RunAsync(server, Secret, id, "echo uno; echo dos; exit 0", TimeSpan.FromSeconds(30), null, null, default);
    Check(ok.Succeeded && Lines(ok.Output).SequenceEqual(["uno", "dos"]), "1. Un paso corto termina bien y devuelve su salida", ok.Output);
    Check(InContainer($"[ -d /home/deploy/.vpsdesk/jobs/{id} ] && echo existe || echo limpio") == "limpio", "1. Los archivos del trabajo se limpian al terminar");

    // ── 2. Fallo propio del comando ──
    var bad = await new DetachedRemoteJob(ssh, fast).RunAsync(server, Secret, DetachedRemoteJob.NewJobId(), "echo antes; echo error >&2; exit 3", TimeSpan.FromSeconds(30), null, null, default);
    Check(!bad.Succeeded && bad.ExitCode == 3 && bad.Output.Contains("antes") && bad.Output.Contains("error") && !bad.Unconfirmed, "2. Un comando que falla informa su código (3) y su salida, sin confundirse con un corte", $"{bad.ExitCode} {bad.Output}");

    string cmd;
    if (On("3")) {
    // ── 3. Corte de red a mitad del paso: el paso sigue en el servidor y se retoma ──
    foreach (var (label, blackhole, downMs) in new[] { ("conexiones cerradas 8 s", false, 8000), ("red caída 25 s (paquetes perdidos)", true, 25000) })
    {
        id = DetachedRemoteJob.NewJobId();
        var statuses = new List<string>();
        var chunks = new System.Text.StringBuilder();
        cmd = $"for i in $(seq 1 20); do echo line $i; sleep 1; done; echo run >> /tmp/runs-{id}";
        CutAfter(proxy, 5000, downMs, blackhole);
        var cut = await new DetachedRemoteJob(ssh, fast).RunAsync(server, Secret, id, cmd, TimeSpan.FromMinutes(3),
            (chunk, _) => chunks.Append(chunk), statuses.Add, default);
        Check(cut.Succeeded, $"3. [{label}] el paso termina bien", $"{cut.ExitCode} unconfirmed={cut.Unconfirmed}");
        Check(Lines(cut.Output).SequenceEqual(expected20), $"3. [{label}] la salida llega completa, en orden y sin repetir líneas", cut.Output);
        Check(Lines(chunks.ToString()).SequenceEqual(expected20), $"3. [{label}] lo mostrado en vivo coincide con la salida completa");
        Check(InContainer($"cat /tmp/runs-{id} | wc -l") == "1", $"3. [{label}] el comando se ejecutó exactamente una vez (no se repitió)");
        if (blackhole)
            Check(statuses.Any(s => s.Contains("Sin conexión")) && statuses.Any(s => s.Contains("Conexión recuperada")), $"3. [{label}] se avisó del corte y de la recuperación", string.Join(" | ", statuses));
    }
    }
    if (On("4")) {
    // ── 4. Corte más largo que la espera: «sin confirmar» y reanudar después ──
    id = DetachedRemoteJob.NewJobId();
    cmd = $"for i in $(seq 1 20); do echo line $i; sleep 1; done; echo run >> /tmp/runs-{id}";
    var shortBudget = fast with { ReconnectBudget = TimeSpan.FromSeconds(6) };
    CutAfter(proxy, 4000, 40000, blackhole: true);
    var lost = await new DetachedRemoteJob(ssh, shortBudget).RunAsync(server, Secret, id, cmd, TimeSpan.FromMinutes(3), null, null, default);
    Check(lost.Unconfirmed && !lost.Succeeded && lost.JobId == id, "4. Si el corte supera la espera, el estado queda «sin confirmar» (no «fallido») y conserva el id del trabajo", $"{lost.ExitCode} unconfirmed={lost.Unconfirmed}");
    Console.WriteLine("      [esperando a que vuelva la red...]");
    await Task.Delay(30000);
    var attached = await new DetachedRemoteJob(ssh, fast).AttachAsync(server, Secret, id, TimeSpan.FromMinutes(1), null, null, default);
    Check(attached.Succeeded && Lines(attached.Output).SequenceEqual(expected20), "4. Al volver la red, se reanuda por el id del trabajo y se recupera todo el resultado", $"{attached.ExitCode} {attached.Output}");
    Check(InContainer($"cat /tmp/runs-{id} | wc -l") == "1", "4. El comando se ejecutó exactamente una vez");
    }
    if (On("5")) {
    // ── 5. El inicio es idempotente por id ──
    id = DetachedRemoteJob.NewJobId();
    var start1 = await ssh.ExecuteAsync(new SshCommandRequest(server, DetachedRemoteJob.BuildStartScript(id, $"echo run >> /tmp/runs-{id}; sleep 1"), TimeSpan.FromSeconds(20)), Secret);
    var start2 = await ssh.ExecuteAsync(new SshCommandRequest(server, DetachedRemoteJob.BuildStartScript(id, $"echo run >> /tmp/runs-{id}; sleep 1"), TimeSpan.FromSeconds(20)), Secret);
    await Task.Delay(3000);
    Check(start1.Succeeded && start2.Succeeded && InContainer($"cat /tmp/runs-{id} | wc -l") == "1", "5. Repetir el inicio (respuesta perdida) no lanza el comando dos veces");

    }
    if (On("6")) {
    // ── 6. Cancelación y plazo detienen el proceso en el servidor ──
    id = DetachedRemoteJob.NewJobId();
    using (var cancellation = new CancellationTokenSource())
    {
        cancellation.CancelAfter(3000);
        var cancelled = await new DetachedRemoteJob(ssh, fast).RunAsync(server, Secret, id, "sleep 301", TimeSpan.FromMinutes(5), null, null, cancellation.Token);
        Check(cancelled.Cancelled, "6. La cancelación se informa como cancelada");
    }
    await Task.Delay(1500);
    Check(InContainer("pgrep -f '[s]leep 301' | wc -l") == "0", "6. Cancelar detiene el proceso en el servidor (no queda huérfano)");
    var timedOut = await new DetachedRemoteJob(ssh, fast).RunAsync(server, Secret, DetachedRemoteJob.NewJobId(), "sleep 302", TimeSpan.FromSeconds(4), null, null, default);
    await Task.Delay(1500);
    Check(timedOut.TimedOut && !timedOut.Succeeded, "6. Superar el plazo se informa como plazo agotado");
    Check(InContainer("pgrep -f '[s]leep 302' | wc -l") == "0", "6. Superar el plazo detiene el proceso en el servidor");

    }
    if (On("7")) {
    // ── 7. Despliegue completo (ComposeDeploymentService) con corte durante «up» ──
    // El commit y la huella los entrega el prevuelo real del producto, igual que en la aplicación.
    var preflight = await new DeploymentPreflightService(ssh).CheckAsync(
        new DeploymentPreflightRequest(server, "/srv/app", "main", "compose.yml", RequireEnvironmentFile: false), Secret);
    var commit = preflight.VerifiedCommit ?? "";
    var digest = preflight.VerifiedFilesDigest ?? "";
    Check(commit.Length >= 40 && digest.Length == 64, "7. El prevuelo real valida el repositorio de prueba (commit y huella)",
        string.Join(" | ", preflight.Checks.Select(c => $"{c.Code}:{c.Status}:{c.Detail}")));
    InContainer("echo 40 > /tmp/up-seconds; echo 0 > /tmp/up-count");
    ComposeDeploymentRequest Deploy(bool detach) => new(server, "/srv/app", "main", "compose.yml", PullImages: true, BuildImages: false,
        CleanBuildCache: false, DeployApplication: true, VerifiedCommit: commit, VerifiedFilesDigest: digest, DetachLongSteps: detach);

    CutAfter(proxy, 9000, 25000, blackhole: true); // cae durante «up» (que tarda 40 s)
    var service = new ComposeDeploymentService(ssh);
    var deploy = await service.ExecuteAsync(Deploy(true), Secret);
    Check(deploy.Succeeded, "7. El despliegue con la red caída 25 s durante «up» termina bien", string.Join(" | ", deploy.Steps.Select(s => $"{s.Code}:{s.Succeeded}:{s.ExitCode}")));
    Check(deploy.Steps.FirstOrDefault(s => s.Code == "compose_up")?.Output.Contains("up done") == true, "7. La salida de «up» llega completa");
    Check(InContainer("cat /tmp/up-count") == "1", "7. «up» se ejecutó exactamente una vez");

    // Contraste: sin desacoplar, el mismo corte rompe el paso.
    InContainer("echo 0 > /tmp/up-count");
    CutAfter(proxy, 9000, 8000);
    var baseline = await new ComposeDeploymentService(ssh).ExecuteAsync(Deploy(false), Secret);
    Check(!baseline.Succeeded, "7. (Contraste) Sin desacoplar, el mismo corte hace fallar el despliegue",
        string.Join(" | ", baseline.Steps.Select(s => $"{s.Code}:{s.Succeeded}:{s.ExitCode}")));
    await Task.Delay(9000);
    Console.WriteLine("INFO  Sin desacoplar, «up» siguió ejecutándose en el servidor tras el corte: " +
        (InContainer("pgrep -f '[d]ocker compose' | wc -l") == "0" ? "no (murió con la sesión)" : "sí"));
    }
}
finally
{
    proxy.Dispose();
    Run("docker", $"rm -f {Container}");
}

Console.WriteLine(failures == 0 ? "\nTODO CORRECTO" : $"\n{failures} COMPROBACIONES FALLARON");
return failures == 0 ? 0 : 1;

/// <summary>
/// Proxy TCP con dos cortes: <see cref="Cut"/> cierra todo y rechaza conexiones nuevas (reinicio); <see cref="Blackhole"/> deja las
/// conexiones abiertas pero sin pasar un solo byte, y acepta conexiones nuevas que se quedan colgadas (como un internet caído:
/// los paquetes se pierden y nada responde). <see cref="Restore"/> vuelve a la normalidad; los datos retenidos se entregan.
/// </summary>
sealed class ToxicProxy(int listenPort, int targetPort) : IDisposable
{
    private TcpListener? _listener;
    private readonly List<TcpClient> _live = [];
    private readonly object _gate = new();
    private CancellationTokenSource _cts = new();
    private volatile bool _blackhole;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, listenPort);
        _listener.Start();
        var listener = _listener;
        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(token); } catch { return; }
                _ = Task.Run(() => Pipe(client, token));
            }
        });
    }

    private async Task Pipe(TcpClient client, CancellationToken token)
    {
        TcpClient target;
        try
        {
            while (_blackhole && !token.IsCancellationRequested) await Task.Delay(100, token); // la conexión nueva se queda colgada
            target = new TcpClient(); await target.ConnectAsync(IPAddress.Loopback, targetPort, token);
        }
        catch { client.Dispose(); return; }
        lock (_gate) { _live.Add(client); _live.Add(target); }
        async Task Copy(TcpClient from, TcpClient to)
        {
            var buffer = new byte[16 * 1024];
            try
            {
                var input = from.GetStream(); var output = to.GetStream();
                while (true)
                {
                    var read = await input.ReadAsync(buffer, token);
                    if (read == 0) break;
                    while (_blackhole && !token.IsCancellationRequested) await Task.Delay(100, token); // retiene los datos
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                }
            }
            catch { }
            finally { try { from.Dispose(); to.Dispose(); } catch { } }
        }
        await Task.WhenAll(Copy(client, target), Copy(target, client));
    }

    public void Cut()
    {
        _cts.Cancel();
        try { _listener?.Stop(); } catch { }
        lock (_gate) { foreach (var c in _live) try { c.Dispose(); } catch { } _live.Clear(); }
        Console.WriteLine("      [red cortada: conexiones cerradas]");
    }

    public void Blackhole()
    {
        _blackhole = true;
        Console.WriteLine("      [red caída: los paquetes se pierden]");
    }

    public void Restore()
    {
        if (_blackhole) _blackhole = false; else Start();
        Console.WriteLine("      [red restaurada]");
    }

    public void Dispose() { _blackhole = false; _cts.Cancel(); try { _listener?.Stop(); } catch { } lock (_gate) { foreach (var c in _live) try { c.Dispose(); } catch { } } }
}

sealed class TracingSsh(ISshCommandExecutor inner) : ISshCommandExecutor
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    public async Task<SshCommandResult> ExecuteAsync(SshCommandRequest request, string? secret, CancellationToken cancellationToken = default)
    {
        var started = Clock.Elapsed.TotalSeconds;
        var result = await inner.ExecuteAsync(request, secret, cancellationToken);
        var kind = request.Command.Contains("@@VPSDESK-STATE") ? "poll " : request.Command.Contains("base64 -d") ? "start" : "other";
        Console.WriteLine($"      [{started,6:0.0}s +{Clock.Elapsed.TotalSeconds - started,4:0.0}s] {kind} exit={result.ExitCode} lost={result.ConnectionLost} timeout={result.TimedOut} out={result.StandardOutput.Length}B err='{result.StandardError.Trim()}'");
        return result;
    }
}
