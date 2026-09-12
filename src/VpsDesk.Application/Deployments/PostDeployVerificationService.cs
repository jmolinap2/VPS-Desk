using VpsDesk.Application.Abstractions;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Application.Deployments;

public enum PostDeployCheckStatus
{
    Passed,
    Warning,
    Failed
}

public sealed record PostDeployCheckResult(
    string Code,
    string Label,
    PostDeployCheckStatus Status,
    string? Detail = null);

public sealed record PostDeployVerificationRequest(
    ServerProfile Server,
    string RemoteRepositoryPath,
    string ComposeFile,
    IReadOnlyList<string> HttpHealthUrls);

public sealed record PostDeployVerificationResult(
    IReadOnlyList<PostDeployCheckResult> Checks,
    DateTimeOffset CheckedAt)
{
    public bool Passed => Checks.All(x => x.Status != PostDeployCheckStatus.Failed);
    public bool HasWarnings => Checks.Any(x => x.Status == PostDeployCheckStatus.Warning);
}

public interface IPostDeployVerificationService
{
    Task<PostDeployVerificationResult> VerifyAsync(
        PostDeployVerificationRequest request,
        string? secret,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Verifies the real state after a Compose deployment. A successful shell exit code alone
/// is not enough: containers may be restarting/unhealthy and HTTP endpoints may still fail.
/// </summary>
public sealed class PostDeployVerificationService(ISshCommandExecutor ssh) : IPostDeployVerificationService
{
    public async Task<PostDeployVerificationResult> VerifyAsync(
        PostDeployVerificationRequest request,
        string? secret,
        CancellationToken cancellationToken = default)
    {
        var checks = new List<PostDeployCheckResult>();
        await VerifyContainersAsync(request, secret, checks, cancellationToken);

        foreach (var rawUrl in request.HttpHealthUrls)
        {
            if (string.IsNullOrWhiteSpace(rawUrl)) continue;
            await VerifyHttpAsync(request.Server, rawUrl.Trim(), secret, checks, cancellationToken);
        }

        return new PostDeployVerificationResult(checks, DateTimeOffset.UtcNow);
    }

    private async Task VerifyContainersAsync(
        PostDeployVerificationRequest request,
        string? secret,
        List<PostDeployCheckResult> checks,
        CancellationToken cancellationToken)
    {
        var repo = DeploymentPreflightService.ShellQuote(request.RemoteRepositoryPath.Trim().TrimEnd('/'));
        var compose = DeploymentPreflightService.ShellQuote(request.ComposeFile.Trim());

        var command =
            $"REPO={repo}; COMPOSE={compose}; " +
            "cd \"$REPO\" || exit 9; " +
            "IDS=\"$(docker compose -f \"$COMPOSE\" ps -aq)\"; " +
            "if [ -z \"$IDS\" ]; then echo __VPSDESK_NO_CONTAINERS__; exit 0; fi; " +
            "docker inspect --format '{{.Name}}|{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' $IDS";

        var result = await ssh.ExecuteAsync(
            new SshCommandRequest(request.Server, command, TimeSpan.FromSeconds(20)),
            secret,
            cancellationToken);

        if (!result.Succeeded)
        {
            checks.Add(new PostDeployCheckResult(
                "compose-state",
                "Docker Compose container state",
                PostDeployCheckStatus.Failed,
                string.IsNullOrWhiteSpace(result.StandardError)
                    ? "Could not inspect the deployed containers."
                    : result.StandardError.Trim()));
            return;
        }

        var lines = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length == 0 || lines.Contains("__VPSDESK_NO_CONTAINERS__", StringComparer.Ordinal))
        {
            checks.Add(new PostDeployCheckResult(
                "compose-state",
                "Docker Compose containers",
                PostDeployCheckStatus.Failed,
                "The selected Compose project has no containers after deployment."));
            return;
        }

        foreach (var line in lines)
        {
            if (line == "__VPSDESK_NO_CONTAINERS__") continue;
            var parts = line.Split('|', 3, StringSplitOptions.TrimEntries);
            if (parts.Length < 2) continue;

            var name = parts[0].Trim().TrimStart('/');
            var state = parts[1].Trim();
            var health = parts.Length >= 3 ? parts[2].Trim() : "none";

            var status = state.Equals("running", StringComparison.OrdinalIgnoreCase)
                ? health.Equals("unhealthy", StringComparison.OrdinalIgnoreCase)
                    ? PostDeployCheckStatus.Failed
                    : health.Equals("starting", StringComparison.OrdinalIgnoreCase)
                        ? PostDeployCheckStatus.Warning
                        : PostDeployCheckStatus.Passed
                : PostDeployCheckStatus.Failed;

            var detail = status switch
            {
                PostDeployCheckStatus.Passed => health.Equals("none", StringComparison.OrdinalIgnoreCase)
                    ? "Running · no Docker healthcheck configured."
                    : $"Running · health {health}.",
                PostDeployCheckStatus.Warning => "Running · Docker healthcheck is still starting.",
                _ => $"State {state} · health {health}. Review the container logs."
            };

            checks.Add(new PostDeployCheckResult(
                $"container:{name}",
                $"Container {name}",
                status,
                detail));
        }
    }

    private async Task VerifyHttpAsync(
        ServerProfile server,
        string rawUrl,
        string? secret,
        List<PostDeployCheckResult> checks,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            checks.Add(new PostDeployCheckResult(
                $"http:{rawUrl}",
                $"HTTP {rawUrl}",
                PostDeployCheckStatus.Failed,
                "Only absolute http:// or https:// health URLs are supported."));
            return;
        }

        var url = DeploymentPreflightService.ShellQuote(uri.AbsoluteUri);
        var command =
            $"URL={url}; " +
            "curl -sS -L -o /dev/null --connect-timeout 5 --max-time 15 -w '%{http_code}' \"$URL\"";

        var result = await ssh.ExecuteAsync(
            new SshCommandRequest(server, command, TimeSpan.FromSeconds(20)),
            secret,
            cancellationToken);

        var codeText = result.StandardOutput.Trim();
        var codeOk = int.TryParse(codeText, out var code) && code is >= 200 and <= 299;
        var status = result.Succeeded && codeOk
            ? PostDeployCheckStatus.Passed
            : PostDeployCheckStatus.Failed;

        var detail = status == PostDeployCheckStatus.Passed
            ? $"HTTP {code}."
            : !string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardError.Trim()
                : string.IsNullOrWhiteSpace(codeText)
                    ? "No HTTP response was received."
                    : $"HTTP {codeText}.";

        checks.Add(new PostDeployCheckResult(
            $"http:{uri.AbsoluteUri}",
            $"HTTP {uri.AbsoluteUri}",
            status,
            detail));
    }
}
