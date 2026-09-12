using System.Globalization;
using VpsDesk.Application.Abstractions;
using VpsDesk.Domain.Servers;
using VpsDesk.Domain.Telemetry;

namespace VpsDesk.Infrastructure.Monitoring;

public sealed class LinuxServerProbeService(ISshCommandExecutor ssh) : IServerProbeService
{
    public async Task<CompatibilitySnapshot> CheckCompatibilityAsync(
        ServerProfile server,
        string? secret,
        CancellationToken cancellationToken = default)
    {
        const string command = "bash -lc \"" +
                               "echo DISTRO=$(source /etc/os-release 2>/dev/null; printf '%s' \"${PRETTY_NAME:-unknown}\"); " +
                               "echo KERNEL=$(uname -sr 2>/dev/null); " +
                               "for c in bash systemctl journalctl docker git nginx; do command -v $c >/dev/null 2>&1 && echo CAP_$c=1 || echo CAP_$c=0; done; " +
                               "docker compose version >/dev/null 2>&1 && echo CAP_compose=1 || echo CAP_compose=0; " +
                               "docker --version 2>/dev/null | sed 's/^/VER_docker=/'; " +
                               "docker compose version 2>/dev/null | sed 's/^/VER_compose=/'; " +
                               "git --version 2>/dev/null | sed 's/^/VER_git=/'; " +
                               "nginx -v 2>&1 | sed 's/^/VER_nginx=/'; " +
                               "systemctl --version 2>/dev/null | head -1 | sed 's/^/VER_systemd=/'\"";

        SshCommandResult result;
        try
        {
            result = await ssh.ExecuteAsync(new SshCommandRequest(server, command, TimeSpan.FromSeconds(12)), secret, cancellationToken);
        }
        catch (Exception ex)
        {
            return Failed(server.Id, ex.Message);
        }

        if (!result.Succeeded)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError) ? "SSH probe failed." : result.StandardError.Trim();
            return Failed(server.Id, detail);
        }

        var values = ParseKeyValues(result.StandardOutput);
        var capabilities = new List<CapabilityResult>
        {
            new(ServerCapability.Ssh, true, Detail: $"{server.Username}@{server.Host}:{server.Port}"),
            new(ServerCapability.Linux, true, Detail: Get(values, "DISTRO")),
            Capability(ServerCapability.Bash, values, "bash"),
            new(ServerCapability.Sftp, true, Detail: "SSH transport connected; SFTP subsystem will be verified on first file operation."),
            Capability(ServerCapability.Systemd, values, "systemctl", Get(values, "VER_systemd")),
            Capability(ServerCapability.Journalctl, values, "journalctl"),
            Capability(ServerCapability.Docker, values, "docker", Get(values, "VER_docker")),
            Capability(ServerCapability.DockerCompose, values, "compose", Get(values, "VER_compose")),
            Capability(ServerCapability.Git, values, "git", Get(values, "VER_git")),
            Capability(ServerCapability.Nginx, values, "nginx", Get(values, "VER_nginx"))
        };

        var essential = capabilities.Where(x => x.Capability is ServerCapability.Ssh or ServerCapability.Linux or ServerCapability.Bash).ToList();
        var level = essential.All(x => x.Available)
            ? IsCertifiedHostinger(server, values)
                ? CompatibilityLevel.Certified
                : CompatibilityLevel.Compatible
            : essential.Any(x => x.Available)
                ? CompatibilityLevel.Partial
                : CompatibilityLevel.Unsupported;

        return new CompatibilitySnapshot(
            server.Id,
            level,
            Get(values, "DISTRO"),
            Get(values, "KERNEL"),
            capabilities,
            DateTimeOffset.UtcNow);
    }

    public async Task<ServerMetrics> ReadMetricsAsync(
        ServerProfile server,
        string? secret,
        CancellationToken cancellationToken = default)
    {
        const string command = "bash -lc \"" +
                               "LC_ALL=C; " +
                               "read cpu u n s i w irq sirq st g gn < /proc/stat; total1=$((u+n+s+i+w+irq+sirq+st)); idle1=$((i+w)); " +
                               "rx1=$(awk -F'[: ]+' 'NR>2 {sum+=$3} END {print sum+0}' /proc/net/dev); " +
                               "tx1=$(awk -F'[: ]+' 'NR>2 {sum+=$11} END {print sum+0}' /proc/net/dev); " +
                               "sleep 1; " +
                               "read cpu u n s i w irq sirq st g gn < /proc/stat; total2=$((u+n+s+i+w+irq+sirq+st)); idle2=$((i+w)); " +
                               "rx2=$(awk -F'[: ]+' 'NR>2 {sum+=$3} END {print sum+0}' /proc/net/dev); " +
                               "tx2=$(awk -F'[: ]+' 'NR>2 {sum+=$11} END {print sum+0}' /proc/net/dev); " +
                               "dt=$((total2-total1)); didle=$((idle2-idle1)); if [ $dt -gt 0 ]; then cpu_pct=$(awk -v dt=$dt -v di=$didle 'BEGIN {printf \"%.2f\", (dt-di)*100/dt}'); else cpu_pct=0; fi; " +
                               "echo CPU=$cpu_pct; " +
                               "awk '{print \"LOAD1=\"$1; print \"LOAD5=\"$2; print \"LOAD15=\"$3}' /proc/loadavg; " +
                               "awk '/MemTotal:/ {mt=$2*1024} /MemAvailable:/ {ma=$2*1024} /SwapTotal:/ {st=$2*1024} /SwapFree:/ {sf=$2*1024} END {print \"MEM_TOTAL=\"mt; print \"MEM_USED=\"mt-ma; print \"SWAP_TOTAL=\"st; print \"SWAP_USED=\"st-sf}' /proc/meminfo; " +
                               "df -B1 --output=size,used / 2>/dev/null | tail -1 | awk '{print \"DISK_TOTAL=\"$1; print \"DISK_USED=\"$2}'; " +
                               "echo RX_BPS=$((rx2-rx1)); echo TX_BPS=$((tx2-tx1)); " +
                               "awk '{print \"UPTIME_SEC=\"int($1)}' /proc/uptime\"";

        var result = await ssh.ExecuteAsync(new SshCommandRequest(server, command, TimeSpan.FromSeconds(15)), secret, cancellationToken);
        if (!result.Succeeded)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError) ? "Unable to read server metrics." : result.StandardError.Trim();
            throw new InvalidOperationException(detail);
        }

        var values = ParseKeyValues(result.StandardOutput);
        return new ServerMetrics(
            server.Id,
            Double(values, "CPU"),
            Double(values, "LOAD1"),
            Double(values, "LOAD5"),
            Double(values, "LOAD15"),
            Long(values, "MEM_USED"),
            Long(values, "MEM_TOTAL"),
            Long(values, "SWAP_USED"),
            Long(values, "SWAP_TOTAL"),
            Long(values, "DISK_USED"),
            Long(values, "DISK_TOTAL"),
            Double(values, "RX_BPS"),
            Double(values, "TX_BPS"),
            TimeSpan.FromSeconds(Double(values, "UPTIME_SEC")),
            DateTimeOffset.UtcNow);
    }

    private static CapabilityResult Capability(
        ServerCapability capability,
        IReadOnlyDictionary<string, string> values,
        string key,
        string? version = null)
        => new(capability,
            values.TryGetValue("CAP_" + key, out var available) && available == "1",
            version,
            null);

    private static CompatibilitySnapshot Failed(Guid serverId, string detail)
        => new(
            serverId,
            CompatibilityLevel.Unsupported,
            null,
            null,
            [new CapabilityResult(ServerCapability.Ssh, false, Detail: detail)],
            DateTimeOffset.UtcNow);

    private static bool IsCertifiedHostinger(ServerProfile server, IReadOnlyDictionary<string, string> values)
    {
        if (!string.Equals(server.ProviderLabel, "Hostinger", StringComparison.OrdinalIgnoreCase)) return false;
        return !string.IsNullOrWhiteSpace(Get(values, "DISTRO"));
    }

    private static Dictionary<string, string> ParseKeyValues(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var index = line.IndexOf('=');
            if (index <= 0) continue;
            result[line[..index].Trim()] = line[(index + 1)..].Trim();
        }

        return result;
    }

    private static string? Get(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static double Double(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;

    private static long Long(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
}
