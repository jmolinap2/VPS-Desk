using VpsDesk.Application.Abstractions;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Application.Deployments;

public sealed record DeploymentDiscoveryResult(
    IReadOnlyList<string> Branches,
    IReadOnlyList<string> ComposeFiles);

public interface IDeploymentDiscoveryService
{
    Task<DeploymentDiscoveryResult> DiscoverAsync(
        ServerProfile server,
        string remoteRepositoryPath,
        string? secret,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Discovers deployable Git branches and Docker Compose files from the remote repository.
/// This is capability-based and provider-agnostic; it does not assume Hostinger or Holos.
/// </summary>
public sealed class DeploymentDiscoveryService(ISshCommandExecutor ssh) : IDeploymentDiscoveryService
{
    public async Task<DeploymentDiscoveryResult> DiscoverAsync(
        ServerProfile server,
        string remoteRepositoryPath,
        string? secret,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(remoteRepositoryPath))
        {
            throw new ArgumentException("Remote repository path is required.", nameof(remoteRepositoryPath));
        }

        var repo = DeploymentPreflightService.ShellQuote(remoteRepositoryPath.Trim().TrimEnd('/'));

        var branchCommand =
            $"bash -lc \"REPO={repo}; " +
            "git -C \\\"$REPO\\\" rev-parse --is-inside-work-tree >/dev/null 2>&1 || exit 9; " +
            "git -C \\\"$REPO\\\" for-each-ref --format='%(refname:short)' refs/heads/ refs/remotes/origin/ " +
            "| sed 's#^origin/##' | grep -v '^HEAD$' | sort -u\"";

        var composeCommand =
            $"bash -lc \"REPO={repo}; " +
            "[ -d \\\"$REPO\\\" ] || exit 9; " +
            "find \\\"$REPO\\\" -maxdepth 1 -type f -printf '%f\\n' " +
            "| grep -E '^(compose.*|docker-compose.*)\\.ya?ml$' | sort -u\"";

        var branchesResult = await ssh.ExecuteAsync(
            new SshCommandRequest(server, branchCommand, TimeSpan.FromSeconds(15)),
            secret,
            cancellationToken);

        if (!branchesResult.Succeeded)
        {
            if (branchesResult.ExitCode == 9)
            {
                throw new InvalidOperationException(
                    $"'{remoteRepositoryPath}' is not a Git working tree on the remote server. Verify REMOTE_REPO_PATH.");
            }

            throw new InvalidOperationException(DescribeFailure(
                branchesResult,
                "Could not discover Git branches in the remote repository."));
        }

        var composeResult = await ssh.ExecuteAsync(
            new SshCommandRequest(server, composeCommand, TimeSpan.FromSeconds(15)),
            secret,
            cancellationToken);

        // grep returns 1 when there are simply no matches. Treat that as an empty list,
        // not as a connection/discovery failure.
        var composeOutput = composeResult.StandardOutput;
        if (!composeResult.Succeeded && !string.IsNullOrWhiteSpace(composeResult.StandardError))
        {
            throw new InvalidOperationException(DescribeFailure(
                composeResult,
                "Could not discover Docker Compose files in the remote repository."));
        }

        return new DeploymentDiscoveryResult(
            SplitLines(branchesResult.StandardOutput),
            SplitLines(composeOutput));
    }

    private static IReadOnlyList<string> SplitLines(string value)
        => value
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string DescribeFailure(SshCommandResult result, string fallback)
        => string.IsNullOrWhiteSpace(result.StandardError)
            ? fallback
            : result.StandardError.Trim();
}
