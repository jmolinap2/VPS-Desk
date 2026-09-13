using VpsDesk.Application.Abstractions;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Application.Deployments;

public sealed record DeploymentDiscoveryResult(
    IReadOnlyList<string> Branches,
    IReadOnlyList<string> ComposeFiles,
    string? CurrentBranch = null);

public sealed record DeploymentProjectCandidate(
    string RemoteRepositoryPath,
    IReadOnlyList<string> ComposeFiles);

public interface IDeploymentDiscoveryService
{
    Task<IReadOnlyList<DeploymentProjectCandidate>> DiscoverProjectsAsync(
        ServerProfile server,
        string? secret,
        CancellationToken cancellationToken = default);

    Task<DeploymentDiscoveryResult> DiscoverAsync(
        ServerProfile server,
        string remoteRepositoryPath,
        string? secret,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> DiscoverServicesAsync(
        ServerProfile server,
        string remoteRepositoryPath,
        string composeFile,
        string? secret,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Discovers deployable Git branches, Docker Compose files and services from the remote repository.
/// This is capability-based and provider-agnostic; it does not assume Hostinger or Holos.
/// </summary>
public sealed class DeploymentDiscoveryService(ISshCommandExecutor ssh) : IDeploymentDiscoveryService
{
    private const string CurrentBranchPrefix = "__VPSDESK_CURRENT_BRANCH__=";

    public async Task<IReadOnlyList<DeploymentProjectCandidate>> DiscoverProjectsAsync(
        ServerProfile server,
        string? secret,
        CancellationToken cancellationToken = default)
    {
        const string command = """
            bash -lc '
            set -o pipefail
            for root in /root /home /opt /srv; do
              [ -d "$root" ] || continue
              find "$root" -xdev -maxdepth 5 -type f \( \
                -name "compose*.yml" -o -name "compose*.yaml" -o \
                -name "docker-compose*.yml" -o -name "docker-compose*.yaml" \
              \) -printf "%h\t%f\n" 2>/dev/null
            done | while IFS="$(printf "\t")" read -r directory compose; do
              repository="$(git -C "$directory" rev-parse --show-toplevel 2>/dev/null)" || continue
              [ "$repository" = "$directory" ] || continue
              printf "%s\t%s\n" "$repository" "$compose"
            done | sort -u
            '
            """;

        var result = await ssh.ExecuteAsync(
            new SshCommandRequest(server, command, TimeSpan.FromSeconds(20)),
            secret,
            cancellationToken);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(DescribeFailure(
                result,
                "Could not discover Docker Compose projects on the remote server."));
        }

        return SplitLines(result.StandardOutput)
            .Select(line => line.Split('\t', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2 &&
                            !string.IsNullOrWhiteSpace(parts[0]) &&
                            !string.IsNullOrWhiteSpace(parts[1]))
            .GroupBy(parts => parts[0], StringComparer.Ordinal)
            .Select(group => new DeploymentProjectCandidate(
                group.Key,
                group.Select(parts => parts[1])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .OrderBy(candidate => candidate.RemoteRepositoryPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

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
            $"REPO={repo}; " +
            "git -C \"$REPO\" rev-parse --is-inside-work-tree >/dev/null 2>&1 || exit 9; " +
            "printf '__VPSDESK_CURRENT_BRANCH__=%s\\n' \"$(git -C \"$REPO\" branch --show-current)\"; " +
            "git -C \"$REPO\" for-each-ref --format='%(refname:short)' refs/heads/ refs/remotes/origin/ " +
            "| sed 's#^origin/##' | grep -v '^HEAD$' | sort -u";

        var composeCommand =
            $"REPO={repo}; " +
            "[ -d \"$REPO\" ] || exit 9; " +
            "find \"$REPO\" -maxdepth 1 -type f -printf '%f\\n' " +
            "| grep -E '^(compose.*|docker-compose.*)\\.ya?ml$' | sort -u";

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

        var composeOutput = composeResult.StandardOutput;
        if (!composeResult.Succeeded && !string.IsNullOrWhiteSpace(composeResult.StandardError))
        {
            throw new InvalidOperationException(DescribeFailure(
                composeResult,
                "Could not discover Docker Compose files in the remote repository."));
        }

        var branchLines = SplitLines(branchesResult.StandardOutput);
        var currentBranch = branchLines
            .FirstOrDefault(line => line.StartsWith(CurrentBranchPrefix, StringComparison.Ordinal))
            ?[CurrentBranchPrefix.Length..];

        return new DeploymentDiscoveryResult(
            branchLines.Where(line => !line.StartsWith(CurrentBranchPrefix, StringComparison.Ordinal)).ToArray(),
            SplitLines(composeOutput),
            string.IsNullOrWhiteSpace(currentBranch) ? null : currentBranch);
    }

    public async Task<IReadOnlyList<string>> DiscoverServicesAsync(
        ServerProfile server,
        string remoteRepositoryPath,
        string composeFile,
        string? secret,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(remoteRepositoryPath))
            throw new ArgumentException("Remote repository path is required.", nameof(remoteRepositoryPath));
        if (string.IsNullOrWhiteSpace(composeFile))
            throw new ArgumentException("Compose file is required.", nameof(composeFile));

        var repo = DeploymentPreflightService.ShellQuote(remoteRepositoryPath.Trim().TrimEnd('/'));
        var compose = DeploymentPreflightService.ShellQuote(composeFile.Trim());
        var command =
            $"REPO={repo}; COMPOSE={compose}; cd \"$REPO\" || exit 9; " +
            "docker compose -f \"$COMPOSE\" config --services";

        var result = await ssh.ExecuteAsync(
            new SshCommandRequest(server, command, TimeSpan.FromSeconds(20)),
            secret,
            cancellationToken);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(DescribeFailure(
                result,
                "Could not discover services from the selected Docker Compose file."));
        }

        return SplitLines(result.StandardOutput);
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
