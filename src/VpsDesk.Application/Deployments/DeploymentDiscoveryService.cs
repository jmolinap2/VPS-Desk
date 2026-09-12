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
            "[ -d \\\"$REPO/.git\\\" ] || exit 9; " +
            "git -C \\\"$REPO\\\" for-each-ref --format='%(refname:short)' refs/heads/ refs/remotes/origin/ " +
            "| sed 's#^origin/##' | grep -v '^HEAD$' | sort -u\"";

        var composeCommand =
            $"bash -lc \"REPO={repo}; " +
            "[ -d \\\"$REPO\\\" ] || exit 9; " +
            "find \\\"$REPO\\\" -maxdepth 1 -type f \\\(" +
            " -name 'compose*.yml' -o -name 'compose*.yaml'" +
            " -o -name 'docker-compose*.yml' -o -name 'docker-compose*.yaml' \\\)" +
            " -printf '%f\\n' | sort -u\"";

        var branchesResult = await ssh.ExecuteAsync(
            new SshCommandRequest(server, branchCommand, TimeSpan.FromSeconds(15)),
            secret,
            cancellationToken);

        if (!branchesResult.Succeeded)
        {
            throw new InvalidOperationException(DescribeFailure(
                branchesResult,
                "Could not discover Git branches in the remote repository."));
        }

        var composeResult = await ssh.ExecuteAsync(
            new SshCommandRequest(server, composeCommand, TimeSpan.FromSeconds(15)),
            secret,
            cancellationToken);

        if (!composeResult.Succeeded)
        {
            throw new InvalidOperationException(DescribeFailure(
                composeResult,
                "Could not discover Docker Compose files in the remote repository."));
        }

        return new DeploymentDiscoveryResult(
            SplitLines(branchesResult.StandardOutput),
            SplitLines(composeResult.StandardOutput));
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
