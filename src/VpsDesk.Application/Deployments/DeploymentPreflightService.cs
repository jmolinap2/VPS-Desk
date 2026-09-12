using VpsDesk.Application.Abstractions;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Application.Deployments;

public enum PreflightCheckStatus
{
    Passed,
    Warning,
    Failed
}

public sealed record DeploymentPreflightRequest(
    ServerProfile Server,
    string RemoteRepositoryPath,
    string? Branch,
    string? ComposeFile,
    bool RequireEnvironmentFile = true,
    string EnvironmentFileName = ".env");

public sealed record PreflightCheckResult(
    string Code,
    string Label,
    PreflightCheckStatus Status,
    string? Detail = null,
    bool IsBlocking = false);

public sealed record DeploymentPreflightResult(
    IReadOnlyList<PreflightCheckResult> Checks,
    DateTimeOffset CheckedAt)
{
    public bool CanProceed => Checks.All(x => x.Status != PreflightCheckStatus.Failed || !x.IsBlocking);
    public bool HasFailures => Checks.Any(x => x.Status == PreflightCheckStatus.Failed);
}

public interface IDeploymentPreflightService
{
    Task<DeploymentPreflightResult> CheckAsync(
        DeploymentPreflightRequest request,
        string? secret,
        CancellationToken cancellationToken = default);
}

public sealed class DeploymentPreflightService(ISshCommandExecutor ssh) : IDeploymentPreflightService
{
    public async Task<DeploymentPreflightResult> CheckAsync(
        DeploymentPreflightRequest request,
        string? secret,
        CancellationToken cancellationToken = default)
    {
        var repo = Quote(request.RemoteRepositoryPath.Trim().TrimEnd('/'));
        var branch = Quote(request.Branch ?? string.Empty);
        var compose = Quote(request.ComposeFile ?? string.Empty);
        var envFile = Quote(request.EnvironmentFileName);

        var script = $"bash -lc \"" +
                     $"REPO={repo}; BRANCH={branch}; COMPOSE={compose}; ENVFILE={envFile}; " +
                     "command -v git >/dev/null 2>&1 && echo GIT=1 || echo GIT=0; " +
                     "command -v docker >/dev/null 2>&1 && echo DOCKER=1 || echo DOCKER=0; " +
                     "docker compose version >/dev/null 2>&1 && echo COMPOSE_CLI=1 || echo COMPOSE_CLI=0; " +
                     "[ -d \"$REPO/.git\" ] && echo REPO=1 || echo REPO=0; " +
                     "if [ -z \"$BRANCH\" ]; then echo BRANCH=2; " +
                     "elif [ -d \"$REPO/.git\" ] && git -C \"$REPO\" rev-parse --verify \"$BRANCH\" >/dev/null 2>&1; then echo BRANCH=1; else echo BRANCH=0; fi; " +
                     "if [ -z \"$COMPOSE\" ]; then echo COMPOSE_FILE=2; elif [ -f \"$REPO/$COMPOSE\" ]; then echo COMPOSE_FILE=1; else echo COMPOSE_FILE=0; fi; " +
                     "[ -f \"$REPO/$ENVFILE\" ] && echo ENVFILE=1 || echo ENVFILE=0\"";

        SshCommandResult result;
        try
        {
            result = await ssh.ExecuteAsync(
                new SshCommandRequest(request.Server, script, TimeSpan.FromSeconds(15)),
                secret,
                cancellationToken);
        }
        catch (Exception ex)
        {
            return new DeploymentPreflightResult(
                [new("ssh", "Conexión SSH", PreflightCheckStatus.Failed, ex.Message, true)],
                DateTimeOffset.UtcNow);
        }

        if (!result.Succeeded)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? "No fue posible ejecutar las verificaciones remotas."
                : result.StandardError.Trim();

            return new DeploymentPreflightResult(
                [new("ssh", "Conexión SSH", PreflightCheckStatus.Failed, detail, true)],
                DateTimeOffset.UtcNow);
        }

        var flags = ParseFlags(result.StandardOutput);
        var checks = new List<PreflightCheckResult>
        {
            new("ssh", "Conexión SSH", PreflightCheckStatus.Passed, $"{request.Server.Username}@{request.Server.Host}"),
            Tool("git", "Git instalado", flags),
            Tool("docker", "Docker instalado", flags),
            Tool("compose_cli", "Docker Compose disponible", flags),
            Flag("repo", $"Repositorio disponible en {request.RemoteRepositoryPath}", flags, true)
        };

        if (!string.IsNullOrWhiteSpace(request.Branch))
        {
            checks.Add(Flag("branch", $"Rama '{request.Branch}' disponible", flags, true));
        }

        if (!string.IsNullOrWhiteSpace(request.ComposeFile))
        {
            checks.Add(Flag("compose_file", $"Archivo {request.ComposeFile} disponible", flags, true));
        }

        if (request.RequireEnvironmentFile)
        {
            checks.Add(Flag("envfile", $"Archivo {request.EnvironmentFileName} disponible", flags, true));
        }

        return new DeploymentPreflightResult(checks, DateTimeOffset.UtcNow);
    }

    private static PreflightCheckResult Tool(
        string code,
        string label,
        IReadOnlyDictionary<string, string> flags)
        => Flag(code, label, flags, true);

    private static PreflightCheckResult Flag(
        string code,
        string label,
        IReadOnlyDictionary<string, string> flags,
        bool blocking)
    {
        var key = code.ToUpperInvariant();
        var ok = flags.TryGetValue(key, out var value) && value == "1";
        return new PreflightCheckResult(
            code,
            label,
            ok ? PreflightCheckStatus.Passed : PreflightCheckStatus.Failed,
            ok ? null : "No se pudo validar este requisito en el servidor.",
            blocking);
    }

    private static Dictionary<string, string> ParseFlags(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var index = raw.IndexOf('=');
            if (index <= 0) continue;
            result[raw[..index].Trim()] = raw[(index + 1)..].Trim();
        }

        return result;
    }

    private static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";
}
