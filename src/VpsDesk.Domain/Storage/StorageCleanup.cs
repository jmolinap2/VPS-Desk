namespace VpsDesk.Domain.Storage;

public enum StorageCleanupKind
{
    BuildCache,
    UnusedImages,
    DockerSystem
}

public sealed record StorageCleanupRequest(
    StorageCleanupKind Kind,
    bool IncludeUnusedVolumes = false);

public sealed record StorageCleanupResult(
    StorageCleanupKind Kind,
    bool IncludeUnusedVolumes,
    bool Succeeded,
    int ExitCode,
    string Output,
    TimeSpan Duration,
    DateTimeOffset FinishedAtUtc);

public sealed record HeavyDirectoryInfo(
    string Path,
    long SizeBytes)
{
    public string SizeLabel
    {
        get
        {
            if (SizeBytes <= 0) return "0 B";
            string[] units = ["B", "KB", "MB", "GB", "TB"];
            var value = (double)SizeBytes;
            var index = 0;
            while (value >= 1024 && index < units.Length - 1)
            {
                value /= 1024;
                index++;
            }

            return $"{value:F1} {units[index]}";
        }
    }
}
