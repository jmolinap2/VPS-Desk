namespace VpsDesk.Domain.Telemetry;

public sealed record ServerMetrics(
    Guid ServerId,
    double CpuUsagePercent,
    double Load1,
    double Load5,
    double Load15,
    long MemoryUsedBytes,
    long MemoryTotalBytes,
    long SwapUsedBytes,
    long SwapTotalBytes,
    long DiskUsedBytes,
    long DiskTotalBytes,
    double NetworkRxBytesPerSecond,
    double NetworkTxBytesPerSecond,
    TimeSpan Uptime,
    DateTimeOffset Timestamp)
{
    public double MemoryUsagePercent => MemoryTotalBytes <= 0 ? 0 : MemoryUsedBytes * 100d / MemoryTotalBytes;
    public double DiskUsagePercent => DiskTotalBytes <= 0 ? 0 : DiskUsedBytes * 100d / DiskTotalBytes;
}
