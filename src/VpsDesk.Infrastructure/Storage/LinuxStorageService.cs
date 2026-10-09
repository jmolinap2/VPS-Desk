using System.Globalization;
using System.Text.Json;
using VpsDesk.Application.Abstractions;
using VpsDesk.Application.Logging;
using VpsDesk.Domain.Servers;
using VpsDesk.Domain.Storage;

namespace VpsDesk.Infrastructure.Storage;

public sealed class LinuxStorageService(ISshCommandExecutor ssh) : IStorageService
{
    public async Task<StorageSnapshot> ReadAsync(
        ServerProfile server,
        string? secret,
        CancellationToken cancellationToken = default)
    {
        var overview = await ReadOverviewAsync(server, secret, cancellationToken);
        var heavyDirectories = await ReadHeavyDirectoriesAsync(server, secret, cancellationToken);
        return overview with { HeavyDirectories = heavyDirectories };
    }

    public async Task<StorageSnapshot> ReadOverviewAsync(
        ServerProfile server,
        string? secret,
        CancellationToken cancellationToken = default)
    {
        var disk = await ssh.ExecuteAsync(
            new SshCommandRequest(
                server,
                "LC_ALL=C df -B1 --output=size,used,avail,pcent / | tail -1",
                TimeSpan.FromSeconds(15)),
            secret,
            cancellationToken);

        if (!disk.Succeeded)
        {
            var detail = string.IsNullOrWhiteSpace(disk.StandardError)
                ? "Unable to read root filesystem usage."
                : disk.StandardError.Trim();
            throw new InvalidOperationException(detail);
        }

        var diskParts = disk.StandardOutput
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var total = ParseLong(diskParts.ElementAtOrDefault(0));
        var used = ParseLong(diskParts.ElementAtOrDefault(1));
        var available = ParseLong(diskParts.ElementAtOrDefault(2));
        var percent = ParsePercent(diskParts.ElementAtOrDefault(3));

        var dockerUsageTask = ExecuteOptionalAsync(
            server,
            secret,
            "docker system df --format '{{json .}}'",
            TimeSpan.FromSeconds(20),
            cancellationToken);
        var imagesTask = ExecuteOptionalAsync(
            server,
            secret,
            "docker image ls --no-trunc --format '{{json .}}'",
            TimeSpan.FromSeconds(20),
            cancellationToken);
        var volumesTask = ExecuteOptionalAsync(
            server,
            secret,
            "docker volume ls --format '{{json .}}'",
            TimeSpan.FromSeconds(20),
            cancellationToken);

        await Task.WhenAll(dockerUsageTask, imagesTask, volumesTask);

        return new StorageSnapshot(
            total,
            used,
            available,
            percent,
            ParseDockerUsage(await dockerUsageTask),
            ParseImages(await imagesTask),
            ParseVolumes(await volumesTask),
            [],
            DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<HeavyDirectoryInfo>> ReadHeavyDirectoriesAsync(
        ServerProfile server,
        string? secret,
        CancellationToken cancellationToken = default)
    {
        var output = await ExecuteOptionalAsync(
            server,
            secret,
            "LC_ALL=C du -x -B1 -d 1 /root /home /var /opt /srv 2>/dev/null | sort -nr | head -25",
            TimeSpan.FromSeconds(45),
            cancellationToken);

        return ParseHeavyDirectories(output);
    }

    public async Task<StorageCleanupResult> CleanupAsync(
        ServerProfile server,
        StorageCleanupRequest request,
        string? secret,
        CancellationToken cancellationToken = default)
    {
        var command = request.Kind switch
        {
            StorageCleanupKind.BuildCache => "docker builder prune --force",
            StorageCleanupKind.UnusedImages => "docker image prune --all --force",
            StorageCleanupKind.DockerSystem => request.IncludeUnusedVolumes
                ? "docker system prune --all --force --volumes"
                : "docker system prune --all --force",
            _ => throw new ArgumentOutOfRangeException(nameof(request.Kind), request.Kind, "Unknown cleanup kind.")
        };

        var timeout = request.Kind == StorageCleanupKind.DockerSystem
            ? TimeSpan.FromMinutes(5)
            : TimeSpan.FromMinutes(3);

        var result = await ssh.ExecuteAsync(
            new SshCommandRequest(server, command, timeout),
            secret,
            cancellationToken);

        var output = CombineOutput(result.StandardOutput, result.StandardError);
        return new StorageCleanupResult(
            request.Kind,
            request.IncludeUnusedVolumes,
            result.Succeeded,
            result.ExitCode,
            LogSanitizer.Sanitize(output),
            result.Duration,
            DateTimeOffset.UtcNow);
    }

    private async Task<string> ExecuteOptionalAsync(
        ServerProfile server,
        string? secret,
        string command,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var result = await ssh.ExecuteAsync(
            new SshCommandRequest(server, command, timeout),
            secret,
            cancellationToken);
        return result.Succeeded ? result.StandardOutput : string.Empty;
    }

    private static IReadOnlyList<DockerDiskUsage> ParseDockerUsage(string output)
    {
        var result = new List<DockerDiskUsage>();
        foreach (var line in SplitLines(output))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                result.Add(new DockerDiskUsage(
                    Get(root, "Type"),
                    Get(root, "TotalCount"),
                    Get(root, "Active"),
                    Get(root, "Size"),
                    Get(root, "Reclaimable")));
            }
            catch (JsonException)
            {
                // Ignore one malformed Docker row and keep the rest of the storage snapshot.
            }
        }

        return result;
    }

    private static IReadOnlyList<DockerImageInfo> ParseImages(string output)
    {
        var result = new List<DockerImageInfo>();
        foreach (var line in SplitLines(output))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                result.Add(new DockerImageInfo(
                    Get(root, "ID"),
                    Get(root, "Repository"),
                    Get(root, "Tag"),
                    Get(root, "Size"),
                    Get(root, "CreatedSince")));
            }
            catch (JsonException)
            {
                // Keep parsing remaining images.
            }
        }

        return result;
    }

    private static IReadOnlyList<DockerVolumeInfo> ParseVolumes(string output)
    {
        var result = new List<DockerVolumeInfo>();
        foreach (var line in SplitLines(output))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                result.Add(new DockerVolumeInfo(
                    Get(root, "Name"),
                    Get(root, "Driver"),
                    Get(root, "Scope")));
            }
            catch (JsonException)
            {
                // Keep parsing remaining volumes.
            }
        }

        return result;
    }

    private static IReadOnlyList<HeavyDirectoryInfo> ParseHeavyDirectories(string output)
    {
        var result = new List<HeavyDirectoryInfo>();
        foreach (var line in SplitLines(output))
        {
            var separator = line.IndexOfAny(['\t', ' ']);
            if (separator <= 0) continue;

            var sizeText = line[..separator].Trim();
            var path = line[(separator + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(path) || !long.TryParse(sizeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size))
            {
                continue;
            }

            result.Add(new HeavyDirectoryInfo(path, size));
        }

        return result;
    }

    private static IEnumerable<string> SplitLines(string value)
        => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Get(JsonElement root, string property)
        => root.TryGetProperty(property, out var value) ? value.ToString() : string.Empty;

    private static long ParseLong(string? value)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static double ParsePercent(string? value)
    {
        value = value?.Trim().TrimEnd('%');
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, 0, 100)
            : 0;
    }

    private static string CombineOutput(string stdout, string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr)) return stdout.TrimEnd();
        if (string.IsNullOrWhiteSpace(stdout)) return stderr.TrimEnd();
        return stdout.TrimEnd() + Environment.NewLine + stderr.TrimEnd();
    }
}
