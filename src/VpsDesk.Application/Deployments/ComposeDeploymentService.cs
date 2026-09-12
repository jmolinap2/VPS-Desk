using VpsDesk.Application.Abstractions;
using VpsDesk.Application.Logging;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Application.Deployments;

public sealed record ComposeDeploymentRequest(
    ServerProfile Server,
    string RemoteRepositoryPath,
    string Branch,
    string ComposeFile,
    bool PullImages = true,
    bool BuildImages = true);

public sealed record DeploymentStepResult(
    string Code,
    string Label,
    bool Succeeded,
    int ExitCode,
    string Output,
    TimeSpan Duration);

public sealed record ComposeDeploymentResult(
    IReadOnlyList<DeploymentStepResult> Steps,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    string? PreviousCommit,
    string? DeployedCommit)
{
    public bool Succeeded => Steps.Count > 0 && Steps.All(x => x.Succeeded);
    public DeploymentStepResult? FailedStep => Steps.FirstOrDefault(x => !x.Succeeded);
}

public interface IComposeDeploymentService
{
    Task<ComposeDeploymentResult> ExecuteAsync(
        ComposeDeploymentRequest request,
        string? secret,
        CancellationToken cancellationToken = default);
}

public sealed class ComposeDeploymentService(ISshCommandExecutor ssh) : IComposeDeploymentService
{
    public async Task<ComposeDeploymentResult> ExecuteAsync(
        ComposeDeploymentRequest request,
        string? secret,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RemoteRepositoryPath))
            throw new ArgumentException("Remote repository path is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Branch))
            throw new ArgumentException("Git branch is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.ComposeFile))
            throw new ArgumentException("Compose file is required.", nameof(request));

        var startedAt = DateTimeOffset.UtcNow;
        var steps = new List<DeploymentStepResult>();
        var repo = DeploymentPreflightService.ShellQuote(request.RemoteRepositoryPath.Trim().TrimEnd('/'));
        var branch = DeploymentPreflightService.ShellQuote(request.Branch.Trim());
        var compose = DeploymentPreflightService.ShellQuote(request.ComposeFile.Trim());
        var remoteBranch = DeploymentPreflightService.ShellQuote("origin/" + request.Branch.Trim());
        var localRef = DeploymentPreflightService.ShellQuote("refs/heads/" + request.Branch.Trim());

        var previousCommit = await TryReadCommitAsync(request, repo, secret, cancellationToken);

        var commands = new List<(string Code, string Label, string Command, TimeSpan Timeout)>
        {
            ("fetch", "Fetch Git", $"git -C {repo} fetch --prune origin", TimeSpan.FromSeconds(60)),
            ("checkout", "Checkout branch",
                $"if git -C {repo} show-ref --verify --quiet {localRef}; then git -C {repo} checkout {branch}; else git -C {repo} checkout -B {branch} {remoteBranch}; fi",
                TimeSpan.FromSeconds(45)),
            ("pull", "Fast-forward source", $"git -C {repo} pull --ff-only origin {branch}", TimeSpan.FromSeconds(90))
        };

        if (request.PullImages)
        {
            commands.Add((
                "compose_pull",
                "Pull container images",
                $"cd -- {repo} && docker compose -f {compose} pull",
                TimeSpan.FromMinutes(5)));
        }

        var buildFlag = request.BuildImages ? " --build" : string.Empty;
        commands.Add((
            "compose_up",
            "Apply Docker Compose",
            $"cd -- {repo} && docker compose -f {compose} up -d{buildFlag}",
            TimeSpan.FromMinutes(10)));
        commands.Add((
            "compose_ps",
            "Read deployment status",
            $"cd -- {repo} && docker compose -f {compose} ps",
            TimeSpan.FromSeconds(45)));

        foreach (var step in commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ssh.ExecuteAsync(
                new SshCommandRequest(request.Server, step.Command, step.Timeout),
                secret,
                cancellationToken);

            var output = CombineOutput(result.StandardOutput, result.StandardError);
            var sanitized = LogSanitizer.Sanitize(output);
            steps.Add(new DeploymentStepResult(
                step.Code,
                step.Label,
                result.Succeeded,
                result.ExitCode,
                sanitized,
                result.Duration));

            if (!result.Succeeded) break;
        }

        var deployedCommit = steps.Any(x => x.Code == "pull" && x.Succeeded)
            ? await TryReadCommitAsync(request, repo, secret, cancellationToken)
            : previousCommit;

        return new ComposeDeploymentResult(
            steps,
            startedAt,
            DateTimeOffset.UtcNow,
            previousCommit,
            deployedCommit);
    }

    private async Task<string?> TryReadCommitAsync(
        ComposeDeploymentRequest request,
        string quotedRepository,
        string? secret,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await ssh.ExecuteAsync(
                new SshCommandRequest(
                    request.Server,
                    $"git -C {quotedRepository} rev-parse HEAD",
                    TimeSpan.FromSeconds(15)),
                secret,
                cancellationToken);

            if (!result.Succeeded) return null;
            var value = LogSanitizer.Sanitize(result.StandardOutput).Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private static string CombineOutput(string stdout, string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr)) return stdout.TrimEnd();
        if (string.IsNullOrWhiteSpace(stdout)) return stderr.TrimEnd();
        return stdout.TrimEnd() + Environment.NewLine + stderr.TrimEnd();
    }
}
