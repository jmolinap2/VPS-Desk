using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using VpsDesk.Application.Abstractions;
using VpsDesk.Domain.Containers;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Infrastructure.Docker;

public sealed class DockerContainerService(ISshCommandExecutor ssh) : IContainerService
{
    public async Task<IReadOnlyList<DockerContainerInfo>> ListAsync(
        ServerProfile server,
        string? secret,
        CancellationToken cancellationToken = default)
    {
        var ps = await ssh.ExecuteAsync(
            new SshCommandRequest(
                server,
                "docker ps -a --no-trunc --format '{{json .}}'",
                TimeSpan.FromSeconds(15)),
            secret,
            cancellationToken);

        if (!ps.Succeeded)
        {
            var detail = string.IsNullOrWhiteSpace(ps.StandardError)
                ? "Docker container inventory could not be read."
                : ps.StandardError.Trim();
            throw new InvalidOperationException(detail);
        }

        var stats = await ssh.ExecuteAsync(
            new SshCommandRequest(
                server,
                "docker stats --no-stream --format '{{json .}}'",
                TimeSpan.FromSeconds(20)),
            secret,
            cancellationToken);

        var statsByName = stats.Succeeded
            ? ParseStats(stats.StandardOutput)
            : new Dictionary<string, ContainerStats>(StringComparer.OrdinalIgnoreCase);

        var result = new List<DockerContainerInfo>();
        foreach (var line in SplitLines(ps.StandardOutput))
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            var id = Get(root, "ID");
            var name = Get(root, "Names");
            var image = Get(root, "Image");
            var state = Get(root, "State");
            var status = Get(root, "Status");
            var ports = Get(root, "Ports");
            var labels = Get(root, "Labels");
            var project = GetLabel(labels, "com.docker.compose.project");
            var health = DetectHealth(status);

            statsByName.TryGetValue(name, out var live);

            result.Add(new DockerContainerInfo(
                id,
                name,
                image,
                state,
                status,
                ports,
                project,
                health,
                live?.CpuPercent ?? 0,
                live?.MemoryUsage ?? "--",
                live?.MemoryPercent ?? 0,
                live?.NetworkIo ?? "--"));
        }

        return result
            .OrderByDescending(x => x.IsRunning)
            .ThenBy(x => x.ComposeProject ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<DockerContainerActionResult> ExecuteAsync(
        ServerProfile server,
        string? secret,
        string containerIdOrName,
        DockerContainerAction action,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(containerIdOrName)
            || !Regex.IsMatch(containerIdOrName, "^[A-Za-z0-9][A-Za-z0-9_.-]*$", RegexOptions.CultureInvariant))
        {
            throw new ArgumentException("Container identifier contains unsupported characters.", nameof(containerIdOrName));
        }

        var verb = action switch
        {
            DockerContainerAction.Start => "start",
            DockerContainerAction.Stop => "stop",
            DockerContainerAction.Restart => "restart",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        var command = $"docker {verb} {containerIdOrName}";
        var result = await ssh.ExecuteAsync(
            new SshCommandRequest(server, command, TimeSpan.FromSeconds(30)),
            secret,
            cancellationToken);

        return new DockerContainerActionResult(
            action,
            containerIdOrName,
            result.Succeeded,
            result.StandardOutput.Trim(),
            result.StandardError.Trim());
    }

    private static Dictionary<string, ContainerStats> ParseStats(string output)
    {
        var result = new Dictionary<string, ContainerStats>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in SplitLines(output))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var name = Get(root, "Name");
                if (string.IsNullOrWhiteSpace(name)) continue;

                result[name] = new ContainerStats(
                    Percent(Get(root, "CPUPerc")),
                    Get(root, "MemUsage"),
                    Percent(Get(root, "MemPerc")),
                    Get(root, "NetIO"));
            }
            catch (JsonException)
            {
                // One malformed Docker row must not hide the rest of the inventory.
            }
        }

        return result;
    }

    private static IEnumerable<string> SplitLines(string value)
        => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Get(JsonElement root, string property)
        => root.TryGetProperty(property, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private static double Percent(string value)
    {
        value = value.Trim().TrimEnd('%');
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? Math.Clamp(result, 0, 100)
            : 0;
    }

    private static string? GetLabel(string labels, string key)
    {
        if (string.IsNullOrWhiteSpace(labels)) return null;
        foreach (var part in labels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0) continue;
            if (!part[..separator].Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            var value = part[(separator + 1)..].Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }

    private static string DetectHealth(string status)
    {
        if (status.Contains("(healthy)", StringComparison.OrdinalIgnoreCase)) return "healthy";
        if (status.Contains("(unhealthy)", StringComparison.OrdinalIgnoreCase)) return "unhealthy";
        if (status.Contains("health: starting", StringComparison.OrdinalIgnoreCase)) return "starting";
        return "none";
    }

    private sealed record ContainerStats(
        double CpuPercent,
        string MemoryUsage,
        double MemoryPercent,
        string NetworkIo);
}
