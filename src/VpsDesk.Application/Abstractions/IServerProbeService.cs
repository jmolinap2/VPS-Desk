using VpsDesk.Domain.Servers;
using VpsDesk.Domain.Telemetry;

namespace VpsDesk.Application.Abstractions;

public interface IServerProbeService
{
    Task<CompatibilitySnapshot> CheckCompatibilityAsync(
        ServerProfile server,
        string? secret,
        CancellationToken cancellationToken = default);

    Task<ServerMetrics> ReadMetricsAsync(
        ServerProfile server,
        string? secret,
        CancellationToken cancellationToken = default);
}
