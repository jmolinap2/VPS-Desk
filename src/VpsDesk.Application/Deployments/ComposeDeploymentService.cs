using System.Text.RegularExpressions;
using VpsDesk.Application.Abstractions;
using VpsDesk.Application.Logging;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Application.Deployments;

public sealed record DeploymentProjectOperationRequest(
    string Code,
    string Label,
    string CommandTemplate,
    string? Input = null,
    int TimeoutSeconds = 600,
    bool IsBlocking = true);

public sealed record ComposeDeploymentRequest(
    ServerProfile Server,
    string RemoteRepositoryPath,
    string Branch,
    string ComposeFile,
    bool PullImages = true,
    bool BuildImages = true,
    bool CleanBuildCache = true,
    bool DeployApplication = true,
    IReadOnlyList<string>? Services = null,
    DeploymentProjectOperationRequest? Operation = null);

public sealed record DeploymentStepResult(
    string Code,
    string Label,
    bool Succeeded,
    int ExitCode,
    string Output,
    TimeSpan Duration,
    bool IsBlocking = true);

public enum DeploymentProgressState
{
    Started,
    Completed
}

public sealed record DeploymentProgressUpdate(
    string Code,
    string Label,
    DeploymentProgressState State,
    bool? Succeeded = null,
    int? ExitCode = null,
    string? Output = null,
    TimeSpan? Duration = null,
    int StepIndex = 0,
    int TotalSteps = 0,
    bool IsBlocking = true);

public sealed record ComposeDeploymentResult(
    IReadOnlyList<DeploymentStepResult> Steps,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    string? PreviousCommit,
    string? DeployedCommit)
{
    public bool Succeeded => Steps.Count > 0 && Steps.Where(x => x.IsBlocking).All(x => x.Succeeded);
    public bool HasWarnings => Steps.Any(x => !x.Succeeded && !x.IsBlocking);
    public DeploymentStepResult? FailedStep => Steps.FirstOrDefault(x => x.IsBlocking && !x.Succeeded);
}

public interface IComposeDeploymentService
{
    event Action<DeploymentProgressUpdate>? ProgressChanged;

    Task<ComposeDeploymentResult> ExecuteAsync(
        ComposeDeploymentRequest request,
        string? secret,
        CancellationToken cancellationToken = default);
}

public sealed class ComposeDeploymentService(ISshCommandExecutor ssh) : IComposeDeploymentService
{
    private static readonly Regex SafeServiceName = new("^[A-Za-z0-9][A-Za-z0-9_.-]*$", RegexOptions.Compiled);
    private static readonly Regex UnresolvedToken = new("\\{\\{[^}]+\\}\\}", RegexOptions.Compiled);

    public event Action<DeploymentProgressUpdate>? ProgressChanged;

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
        if (!request.DeployApplication && request.Operation is null)
            throw new ArgumentException("The request must deploy the application or execute a project operation.", nameof(request));

        var startedAt = DateTimeOffset.UtcNow;
        var steps = new List<DeploymentStepResult>();
        var repo = DeploymentPreflightService.ShellQuote(request.RemoteRepositoryPath.Trim().TrimEnd('/'));
        var branch = DeploymentPreflightService.ShellQuote(request.Branch.Trim());
        var compose = DeploymentPreflightService.ShellQuote(request.ComposeFile.Trim());
        var remoteBranch = DeploymentPreflightService.ShellQuote("origin/" + request.Branch.Trim());
        var localRef = DeploymentPreflightService.ShellQuote("refs/heads/" + request.Branch.Trim());
        var serviceArguments = BuildServiceArguments(request.Services);

        var previousCommit = await TryReadCommitAsync(request, repo, secret, cancellationToken);

        var commands = new List<(string Code, string Label, string Command, TimeSpan Timeout, bool IsBlocking)>
        {
            ("fetch", "Fetch Git", $"git -C {repo} fetch --prune origin", TimeSpan.FromSeconds(60), true),
            ("checkout", "Checkout branch",
                $"if git -C {repo} show-ref --verify --quiet {localRef}; then git -C {repo} checkout {branch}; else git -C {repo} checkout -B {branch} {remoteBranch}; fi",
                TimeSpan.FromSeconds(45), true),
            ("pull", "Fast-forward source", $"git -C {repo} pull --ff-only origin {branch}", TimeSpan.FromSeconds(90), true)
        };

        if (request.DeployApplication && request.PullImages)
        {
            commands.Add((
                "compose_pull",
                "Pull container images",
                $"cd -- {repo} && docker compose -f {compose} pull{serviceArguments}",
                TimeSpan.FromMinutes(5),
                true));
        }

        if (request.DeployApplication)
        {
            var buildFlag = request.BuildImages ? " --build" : string.Empty;
            commands.Add((
                "compose_up",
                "Apply Docker Compose",
                $"cd -- {repo} && docker compose -f {compose} up -d{buildFlag}{serviceArguments}",
                TimeSpan.FromMinutes(10),
                true));
        }

        if (request.Operation is not null)
        {
            var operation = request.Operation;
            commands.Add((
                operation.Code,
                operation.Label,
                RenderOperationCommand(operation, request),
                TimeSpan.FromSeconds(Math.Clamp(operation.TimeoutSeconds, 10, 3600)),
                operation.IsBlocking));
        }

        commands.Add((
            "compose_ps",
            "Read deployment status",
            $"cd -- {repo} && docker compose -f {compose} ps{serviceArguments}",
            TimeSpan.FromSeconds(45),
            true));

        if (request.DeployApplication && request.CleanBuildCache)
        {
            commands.Add((
                "build_cache_prune",
                "Clean Docker build cache",
                "docker builder prune -f",
                TimeSpan.FromMinutes(3),
                false));
        }

        for (var index = 0; index < commands.Count; index++)
        {
            var step = commands[index];
            var stepIndex = index + 1;
            cancellationToken.ThrowIfCancellationRequested();
            ProgressChanged?.Invoke(new DeploymentProgressUpdate(
                step.Code,
                step.Label,
                DeploymentProgressState.Started,
                StepIndex: stepIndex,
                TotalSteps: commands.Count,
                IsBlocking: step.IsBlocking));

            var result = await ssh.ExecuteAsync(
                new SshCommandRequest(request.Server, step.Command, step.Timeout),
                secret,
                cancellationToken);

            var output = CombineOutput(result.StandardOutput, result.StandardError);
            var sanitized = LogSanitizer.Sanitize(output);
            var stepResult = new DeploymentStepResult(
                step.Code,
                step.Label,
                result.Succeeded,
                result.ExitCode,
                sanitized,
                result.Duration,
                step.IsBlocking);
            steps.Add(stepResult);

            ProgressChanged?.Invoke(new DeploymentProgressUpdate(
                step.Code,
                step.Label,
                DeploymentProgressState.Completed,
                stepResult.Succeeded,
                stepResult.ExitCode,
                stepResult.Output,
                stepResult.Duration,
                stepIndex,
                commands.Count,
                step.IsBlocking));

            if (!result.Succeeded && step.IsBlocking) break;
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

    private static string BuildServiceArguments(IReadOnlyList<string>? services)
    {
        if (services is null || services.Count == 0) return string.Empty;

        var normalized = services
            .Where(service => !string.IsNullOrWhiteSpace(service))
            .Select(service => service.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var service in normalized)
        {
            if (!SafeServiceName.IsMatch(service))
            {
                throw new InvalidOperationException($"Invalid Docker Compose service name: '{service}'.");
            }
        }

        return normalized.Length == 0
            ? string.Empty
            : " " + string.Join(" ", normalized.Select(DeploymentPreflightService.ShellQuote));
    }

    private static string RenderOperationCommand(
        DeploymentProjectOperationRequest operation,
        ComposeDeploymentRequest request)
    {
        if (string.IsNullOrWhiteSpace(operation.Code) || string.IsNullOrWhiteSpace(operation.Label))
            throw new InvalidOperationException("Project operation code and label are required.");
        if (string.IsNullOrWhiteSpace(operation.CommandTemplate))
            throw new InvalidOperationException($"Project operation '{operation.Label}' has no command template.");

        var command = operation.CommandTemplate
            .Replace("{{compose}}", DeploymentPreflightService.ShellQuote(request.ComposeFile.Trim()), StringComparison.Ordinal)
            .Replace("{{repository}}", DeploymentPreflightService.ShellQuote(request.RemoteRepositoryPath.Trim().TrimEnd('/')), StringComparison.Ordinal)
            .Replace("{{branch}}", DeploymentPreflightService.ShellQuote(request.Branch.Trim()), StringComparison.Ordinal)
            .Replace("{{input}}", DeploymentPreflightService.ShellQuote(operation.Input ?? string.Empty), StringComparison.Ordinal);

        var unresolved = UnresolvedToken.Match(command);
        if (unresolved.Success)
        {
            throw new InvalidOperationException(
                $"Project operation '{operation.Label}' contains unsupported template token {unresolved.Value}.");
        }

        return $"cd -- {DeploymentPreflightService.ShellQuote(request.RemoteRepositoryPath.Trim().TrimEnd('/'))} && {command}";
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
