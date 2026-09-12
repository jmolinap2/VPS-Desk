using System.Text.RegularExpressions;
using VpsDesk.Application.Abstractions;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Infrastructure.Logs;

public sealed class LinuxRemoteLogService(ISshCommandExecutor ssh) : IRemoteLogService
{
    public async Task<IReadOnlyList<string>> ListSystemdServicesAsync(
        ServerProfile server,
        string? secret,
        CancellationToken cancellationToken = default)
    {
        var result = await ssh.ExecuteAsync(
            new SshCommandRequest(
                server,
                "systemctl list-units --type=service --all --no-legend --no-pager | awk '{print $1}'",
                TimeSpan.FromSeconds(15)),
            secret,
            cancellationToken);

        if (!result.Succeeded)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? "Unable to enumerate systemd services."
                : result.StandardError.Trim();
            throw new InvalidOperationException(detail);
        }

        return result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsSafeUnitName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<string> ReadContainerLogsAsync(
        ServerProfile server,
        string? secret,
        string containerIdOrName,
        int tailLines,
        CancellationToken cancellationToken = default)
    {
        if (!IsSafeContainerIdentifier(containerIdOrName))
        {
            throw new ArgumentException("Container identifier contains unsupported characters.", nameof(containerIdOrName));
        }

        tailLines = Math.Clamp(tailLines, 20, 5000);
        var result = await ssh.ExecuteAsync(
            new SshCommandRequest(
                server,
                $"docker logs --timestamps --tail {tailLines} {containerIdOrName}",
                TimeSpan.FromSeconds(25)),
            secret,
            cancellationToken);

        if (!result.Succeeded)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? "Unable to read Docker logs."
                : result.StandardError.Trim();
            throw new InvalidOperationException(detail);
        }

        return JoinOutput(result.StandardOutput, result.StandardError);
    }

    public async Task<string> ReadSystemdLogsAsync(
        ServerProfile server,
        string? secret,
        string serviceName,
        int tailLines,
        CancellationToken cancellationToken = default)
    {
        if (!IsSafeUnitName(serviceName))
        {
            throw new ArgumentException("Systemd service name contains unsupported characters.", nameof(serviceName));
        }

        tailLines = Math.Clamp(tailLines, 20, 5000);
        var result = await ssh.ExecuteAsync(
            new SshCommandRequest(
                server,
                $"journalctl --no-pager -u {serviceName} -n {tailLines} -o short-iso",
                TimeSpan.FromSeconds(25)),
            secret,
            cancellationToken);

        if (!result.Succeeded)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? "Unable to read journalctl logs."
                : result.StandardError.Trim();
            throw new InvalidOperationException(detail);
        }

        return JoinOutput(result.StandardOutput, result.StandardError);
    }

    private static string JoinOutput(string stdout, string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr)) return stdout.TrimEnd();
        if (string.IsNullOrWhiteSpace(stdout)) return stderr.TrimEnd();
        return stdout.TrimEnd() + Environment.NewLine + stderr.TrimEnd();
    }

    private static bool IsSafeContainerIdentifier(string value)
        => !string.IsNullOrWhiteSpace(value)
           && Regex.IsMatch(value, "^[A-Za-z0-9][A-Za-z0-9_.-]*$", RegexOptions.CultureInvariant);

    private static bool IsSafeUnitName(string value)
        => !string.IsNullOrWhiteSpace(value)
           && Regex.IsMatch(value, "^[A-Za-z0-9@_.:-]+$", RegexOptions.CultureInvariant);
}
