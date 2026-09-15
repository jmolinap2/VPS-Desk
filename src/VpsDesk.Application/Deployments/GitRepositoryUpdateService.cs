using VpsDesk.Application.Abstractions;
using VpsDesk.Application.Logging;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Application.Deployments;

public sealed record GitRepositoryUpdateRequest(
    ServerProfile Server,
    string RemoteRepositoryPath,
    string Branch);

public sealed record GitRepositoryUpdateResult(
    IReadOnlyList<DeploymentStepResult> Steps,
    string? PreviousCommit,
    string? UpdatedCommit)
{
    public bool Succeeded => Steps.Count > 0 && Steps.All(step => step.Succeeded);
    public DeploymentStepResult? FailedStep => Steps.FirstOrDefault(step => !step.Succeeded);
    public bool HasChanges => Succeeded
                              && !string.Equals(PreviousCommit, UpdatedCommit, StringComparison.OrdinalIgnoreCase);
}

public enum GitRepositoryUpdateProgressState
{
    Started,
    Completed
}

public sealed record GitRepositoryUpdateProgressUpdate(
    string Label,
    GitRepositoryUpdateProgressState State,
    bool? Succeeded = null,
    int? ExitCode = null,
    string? Output = null,
    TimeSpan? Duration = null);

public interface IGitRepositoryUpdateService
{
    event Action<GitRepositoryUpdateProgressUpdate>? ProgressChanged;

    Task<GitRepositoryUpdateResult> PullAsync(
        GitRepositoryUpdateRequest request,
        string? secret,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Updates the checked-out source without touching Docker Compose or running project operations.
/// </summary>
public sealed class GitRepositoryUpdateService(ISshCommandExecutor ssh) : IGitRepositoryUpdateService
{
    public event Action<GitRepositoryUpdateProgressUpdate>? ProgressChanged;

    public async Task<GitRepositoryUpdateResult> PullAsync(
        GitRepositoryUpdateRequest request,
        string? secret,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RemoteRepositoryPath))
            throw new ArgumentException("Remote repository path is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Branch))
            throw new ArgumentException("Git branch is required.", nameof(request));

        var repo = DeploymentPreflightService.ShellQuote(request.RemoteRepositoryPath.Trim().TrimEnd('/'));
        var branchName = request.Branch.Trim();
        var branch = DeploymentPreflightService.ShellQuote(branchName);
        var remoteBranch = DeploymentPreflightService.ShellQuote("origin/" + branchName);
        var localRef = DeploymentPreflightService.ShellQuote("refs/heads/" + branchName);
        var previousCommit = await TryReadCommitAsync(request, repo, secret, cancellationToken);
        var steps = new List<DeploymentStepResult>();

        var commands = new (string Code, string Label, string Command, TimeSpan Timeout)[]
        {
            ("fetch", "Fetch Git", $"git -C {repo} fetch --prune origin", TimeSpan.FromSeconds(60)),
            ("checkout", "Checkout branch",
                $"if git -C {repo} show-ref --verify --quiet {localRef}; then git -C {repo} checkout {branch}; else git -C {repo} checkout -B {branch} {remoteBranch}; fi",
                TimeSpan.FromSeconds(45)),
            ("pull", "Fast-forward source", $"git -C {repo} pull --ff-only origin {branch}", TimeSpan.FromSeconds(90))
        };

        foreach (var step in commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProgressChanged?.Invoke(new GitRepositoryUpdateProgressUpdate(
                step.Label,
                GitRepositoryUpdateProgressState.Started));
            var result = await ssh.ExecuteAsync(
                new SshCommandRequest(request.Server, step.Command, step.Timeout),
                secret,
                cancellationToken);

            var output = CombineOutput(result.StandardOutput, result.StandardError);
            var completedStep = new DeploymentStepResult(
                step.Code,
                step.Label,
                result.Succeeded,
                result.ExitCode,
                LogSanitizer.Sanitize(output),
                result.Duration);
            steps.Add(completedStep);
            ProgressChanged?.Invoke(new GitRepositoryUpdateProgressUpdate(
                step.Label,
                GitRepositoryUpdateProgressState.Completed,
                completedStep.Succeeded,
                completedStep.ExitCode,
                completedStep.Output,
                completedStep.Duration));

            if (!result.Succeeded) break;
        }

        var updatedCommit = steps.Any(step => step.Code == "pull" && step.Succeeded)
            ? await TryReadCommitAsync(request, repo, secret, cancellationToken)
            : previousCommit;

        return new GitRepositoryUpdateResult(steps, previousCommit, updatedCommit);
    }

    private async Task<string?> TryReadCommitAsync(
        GitRepositoryUpdateRequest request,
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
            var commit = LogSanitizer.Sanitize(result.StandardOutput).Trim();
            return string.IsNullOrWhiteSpace(commit) ? null : commit;
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
