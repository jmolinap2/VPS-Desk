using VpsDesk.Application.Abstractions;
using VpsDesk.Domain.Security;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Infrastructure.Security;

public sealed class LinuxSecurityAuditService(ISshCommandExecutor ssh) : ISecurityAuditService
{
    private static readonly IReadOnlyDictionary<int, string> SensitivePorts = new Dictionary<int, string>
    {
        [1433] = "SQL Server",
        [3306] = "MySQL/MariaDB",
        [5432] = "PostgreSQL",
        [6379] = "Redis",
        [27017] = "MongoDB",
        [2375] = "Docker API (unencrypted)",
        [2376] = "Docker API"
    };

    public async Task<SecurityAuditSnapshot> AuditAsync(
        ServerProfile server,
        string? secret,
        CancellationToken cancellationToken = default)
    {
        const string command =
            "echo __LISTEN__; " +
            "command -v ss >/dev/null 2>&1 && ss -lntH 2>/dev/null || true; " +
            "echo __SSHD__; " +
            "command -v sshd >/dev/null 2>&1 && sshd -T 2>/dev/null | grep -E '^(passwordauthentication|permitrootlogin) ' || true; " +
            "echo __UFW__; " +
            "command -v ufw >/dev/null 2>&1 && ufw status 2>/dev/null | head -5 || true; " +
            "echo __FIREWALLD__; " +
            "command -v firewall-cmd >/dev/null 2>&1 && firewall-cmd --state 2>/dev/null || true; " +
            "echo __FAIL2BAN__; " +
            "command -v systemctl >/dev/null 2>&1 && systemctl is-active fail2ban 2>/dev/null || true";

        var result = await ssh.ExecuteAsync(
            new SshCommandRequest(server, command, TimeSpan.FromSeconds(18)),
            secret,
            cancellationToken);

        if (!result.Succeeded)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? "Unable to run the read-only security audit."
                : result.StandardError.Trim();
            throw new InvalidOperationException(detail);
        }

        var sections = ParseSections(result.StandardOutput);
        var checks = new List<SecurityCheck>();

        AddLocalProfileChecks(server, checks);
        AddSshdChecks(sections.GetValueOrDefault("SSHD") ?? string.Empty, checks);
        AddListeningPortChecks(sections.GetValueOrDefault("LISTEN") ?? string.Empty, checks);
        AddFirewallChecks(
            sections.GetValueOrDefault("UFW") ?? string.Empty,
            sections.GetValueOrDefault("FIREWALLD") ?? string.Empty,
            checks);
        AddFail2BanCheck(sections.GetValueOrDefault("FAIL2BAN") ?? string.Empty, checks);

        return new SecurityAuditSnapshot(checks, DateTimeOffset.UtcNow);
    }

    private static void AddLocalProfileChecks(ServerProfile server, ICollection<SecurityCheck> checks)
    {
        checks.Add(server.AuthenticationType == SshAuthenticationType.PrivateKey
            ? new SecurityCheck("ssh.auth", "SSH key authentication", SecurityCheckSeverity.Passed, "The VPS Desk profile uses a private key.")
            : new SecurityCheck("ssh.auth", "SSH key authentication", SecurityCheckSeverity.Warning, "The profile uses password authentication. Prefer a private key when possible."));

        checks.Add(!string.IsNullOrWhiteSpace(server.HostKeyFingerprintSha256)
            ? new SecurityCheck("ssh.pin", "SSH host key pinning", SecurityCheckSeverity.Passed, "A SHA256 host fingerprint is pinned in this local profile.")
            : new SecurityCheck("ssh.pin", "SSH host key pinning", SecurityCheckSeverity.Warning, "No host fingerprint is pinned; configure one after verifying it through a trusted source."));

        checks.Add(string.Equals(server.Username, "root", StringComparison.OrdinalIgnoreCase)
            ? new SecurityCheck("ssh.user", "Administrative SSH user", SecurityCheckSeverity.Warning, "VPS Desk is connecting directly as root. A restricted administration account with controlled sudo is safer.")
            : new SecurityCheck("ssh.user", "Administrative SSH user", SecurityCheckSeverity.Passed, $"VPS Desk connects as '{server.Username}', not directly as root."));
    }

    private static void AddSshdChecks(string section, ICollection<SecurityCheck> checks)
    {
        var values = section
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);

        if (values.TryGetValue("passwordauthentication", out var passwordAuth))
        {
            checks.Add(passwordAuth.Equals("no", StringComparison.OrdinalIgnoreCase)
                ? new SecurityCheck("sshd.password", "Server password authentication", SecurityCheckSeverity.Passed, "sshd reports PasswordAuthentication no.")
                : new SecurityCheck("sshd.password", "Server password authentication", SecurityCheckSeverity.Warning, $"sshd reports PasswordAuthentication {passwordAuth}."));
        }
        else
        {
            checks.Add(new SecurityCheck("sshd.password", "Server password authentication", SecurityCheckSeverity.Info, "Could not determine the effective sshd PasswordAuthentication setting without changing the server."));
        }

        if (values.TryGetValue("permitrootlogin", out var rootLogin))
        {
            var severity = rootLogin.Equals("no", StringComparison.OrdinalIgnoreCase)
                ? SecurityCheckSeverity.Passed
                : rootLogin.Equals("yes", StringComparison.OrdinalIgnoreCase)
                    ? SecurityCheckSeverity.Warning
                    : SecurityCheckSeverity.Info;
            checks.Add(new SecurityCheck("sshd.root", "Remote root login policy", severity, $"sshd reports PermitRootLogin {rootLogin}."));
        }
        else
        {
            checks.Add(new SecurityCheck("sshd.root", "Remote root login policy", SecurityCheckSeverity.Info, "Could not determine the effective PermitRootLogin setting."));
        }
    }

    private static void AddListeningPortChecks(string section, ICollection<SecurityCheck> checks)
    {
        var publicPorts = new SortedSet<int>();
        foreach (var line in section.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (fields.Length < 4) continue;
            if (!TryParseEndpoint(fields[3], out var host, out var port)) continue;
            if (IsPublicBind(host)) publicPorts.Add(port);
        }

        if (publicPorts.Count == 0)
        {
            checks.Add(new SecurityCheck("ports.public", "Public TCP listeners", SecurityCheckSeverity.Info, "No public TCP listeners were detected, or the 'ss' utility is unavailable."));
            return;
        }

        checks.Add(new SecurityCheck(
            "ports.public",
            "Public TCP listeners",
            SecurityCheckSeverity.Info,
            $"Detected {publicPorts.Count} public TCP port(s): {string.Join(", ", publicPorts)}."));

        foreach (var port in publicPorts)
        {
            if (!SensitivePorts.TryGetValue(port, out var service)) continue;
            var severity = port == 2375 || port is 1433 or 3306 or 5432 or 6379 or 27017
                ? SecurityCheckSeverity.Critical
                : SecurityCheckSeverity.Warning;
            checks.Add(new SecurityCheck(
                $"port.{port}",
                $"Public {service} port",
                severity,
                $"TCP {port} is bound on a public/wildcard address. Verify that exposure is intentional and protected by a firewall or private network."));
        }
    }

    private static void AddFirewallChecks(string ufw, string firewalld, ICollection<SecurityCheck> checks)
    {
        if (ufw.Contains("Status: active", StringComparison.OrdinalIgnoreCase))
        {
            checks.Add(new SecurityCheck("firewall", "Host firewall", SecurityCheckSeverity.Passed, "UFW reports an active firewall."));
            return;
        }

        if (firewalld.Trim().Equals("running", StringComparison.OrdinalIgnoreCase))
        {
            checks.Add(new SecurityCheck("firewall", "Host firewall", SecurityCheckSeverity.Passed, "firewalld reports running."));
            return;
        }

        if (ufw.Contains("Status: inactive", StringComparison.OrdinalIgnoreCase)
            || firewalld.Trim().Equals("not running", StringComparison.OrdinalIgnoreCase))
        {
            checks.Add(new SecurityCheck("firewall", "Host firewall", SecurityCheckSeverity.Warning, "A detected host firewall is inactive. Provider-level firewalling may still exist, so verify the VPS provider rules too."));
            return;
        }

        checks.Add(new SecurityCheck("firewall", "Host firewall", SecurityCheckSeverity.Info, "No active UFW/firewalld status could be confirmed. This does not prove that the server is unprotected."));
    }

    private static void AddFail2BanCheck(string fail2ban, ICollection<SecurityCheck> checks)
    {
        checks.Add(fail2ban.Trim().Equals("active", StringComparison.OrdinalIgnoreCase)
            ? new SecurityCheck("fail2ban", "Fail2ban", SecurityCheckSeverity.Passed, "fail2ban is active.")
            : new SecurityCheck("fail2ban", "Fail2ban", SecurityCheckSeverity.Info, "fail2ban was not confirmed active. It is optional and may be replaced by provider/network controls."));
    }

    private static Dictionary<string, string> ParseSections(string output)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? current = null;
        var buffer = new List<string>();

        void Flush()
        {
            if (current != null) result[current] = string.Join(Environment.NewLine, buffer);
            buffer.Clear();
        }

        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.None))
        {
            if (line.StartsWith("__", StringComparison.Ordinal) && line.EndsWith("__", StringComparison.Ordinal) && line.Length > 4)
            {
                Flush();
                current = line.Trim('_');
                continue;
            }

            if (current != null) buffer.Add(line);
        }
        Flush();
        return result;
    }

    private static bool TryParseEndpoint(string endpoint, out string host, out int port)
    {
        host = string.Empty;
        port = 0;
        var separator = endpoint.LastIndexOf(':');
        if (separator < 0 || separator == endpoint.Length - 1) return false;
        host = endpoint[..separator].Trim('[', ']');
        return int.TryParse(endpoint[(separator + 1)..], out port);
    }

    private static bool IsPublicBind(string host)
        => host is "0.0.0.0" or "*" or "::" or "[::]" || string.IsNullOrWhiteSpace(host);
}
