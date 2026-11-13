using VpsDesk.Application.Abstractions;
using VpsDesk.Application.Deployments;
using VpsDesk.Domain.Servers;

var server = new ServerProfile(Guid.NewGuid(), "Test", "localhost", 22, "test",
    SshAuthenticationType.Password, null, null, null, ServerEnvironment.Development, []);
var request = new ComposeDeploymentRequest(server, "/app", "main", "compose.yml",
    PullImages: false, BuildImages: false, CleanBuildCache: false);
static SshCommandResult Ok() => new(0, "running", "", TimeSpan.Zero, false, false);
static SshCommandResult Lost() => new(-1, "", "Software caused connection abort", TimeSpan.Zero, false, false, true);
static void Check(bool passed, string label)
{
    if (!passed) throw new Exception(label);
    Console.WriteLine("PASS " + label);
}

var ssh = new FakeSsh((command, count) => command.Contains(" ps") && count == 1 ? Lost() : Ok());
var progress = new List<DeploymentProgressUpdate>();
var service = new ComposeDeploymentService(ssh);
service.ProgressChanged += progress.Add;
var result = await service.ExecuteAsync(request, null);
Check(result.Succeeded && ssh.Count(" ps") == 2 && ssh.Count(" up ") == 1, "Reconnect status without replaying deployment");
Check(progress.Any(p => p.Output?.Contains("Reintentando") == true), "Publish reconnection progress");

ssh = new FakeSsh((command, _) => command.Contains(" ps") ? Lost() : Ok());
result = await new ComposeDeploymentService(ssh).ExecuteAsync(request, null);
Check(result.Succeeded && result.HasWarnings && ssh.Count(" ps") == 3, "Bound retries and preserve completed deployment with warning");

ssh = new FakeSsh((command, _) => command.Contains(" ps")
    ? new(-1, "", "Authentication rejected", TimeSpan.Zero, false, false) : Ok());
result = await new ComposeDeploymentService(ssh).ExecuteAsync(request, null);
Check(ssh.Count(" ps") == 1, "Do not retry authentication or logical errors");

ssh = new FakeSsh((command, _) => command.Contains(" up ") ? Lost() : Ok());
result = await new ComposeDeploymentService(ssh).ExecuteAsync(request, null);
Check(!result.Succeeded && ssh.Count(" up ") == 1, "Never replay interrupted compose up");

ssh = new FakeSsh((_, count) => count == 1 ? Lost() : Ok());
await SshReadRetry.ExecuteAsync(ssh, new(server, "inspect", TimeSpan.FromSeconds(5)), null);
Check(ssh.Count("inspect") == 2, "Retry verification reads");

using var cancellation = new CancellationTokenSource();
ssh = new FakeSsh((_, _) => Lost());
try
{
    await SshReadRetry.ExecuteAsync(ssh, new(server, "inspect", TimeSpan.FromSeconds(5)), null,
        cancellation.Token, _ => cancellation.Cancel());
    throw new Exception("Cancellation was ignored");
}
catch (OperationCanceledException) { Check(ssh.Count("inspect") == 1, "Cancellation stops retries"); }

sealed class FakeSsh(Func<string, int, SshCommandResult> respond) : ISshCommandExecutor
{
    private readonly List<string> commands = [];
    public int Count(string text) => commands.Count(c => c.Contains(text));
    public Task<SshCommandResult> ExecuteAsync(SshCommandRequest request, string? secret, CancellationToken cancellationToken = default)
    {
        commands.Add(request.Command);
        return Task.FromResult(respond(request.Command, commands.Count(c => c == request.Command)));
    }
}
