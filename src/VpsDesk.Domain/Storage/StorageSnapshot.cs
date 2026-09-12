namespace VpsDesk.Domain.Storage;

public sealed record StorageSnapshot(
    long RootTotalBytes,
    long RootUsedBytes,
    long RootAvailableBytes,
    double RootUsagePercent,
    IReadOnlyList<DockerDiskUsage> DockerUsage,
    IReadOnlyList<DockerImageInfo> Images,
    IReadOnlyList<DockerVolumeInfo> Volumes,
    DateTimeOffset CollectedAtUtc);

public sealed record DockerDiskUsage(
    string Type,
    string Total,
    string Active,
    string Size,
    string Reclaimable);

public sealed record DockerImageInfo(
    string Id,
    string Repository,
    string Tag,
    string Size,
    string CreatedSince)
{
    public string DisplayName => Repository == "<none>" ? Id : $"{Repository}:{Tag}";
}

public sealed record DockerVolumeInfo(
    string Name,
    string Driver,
    string Scope);
