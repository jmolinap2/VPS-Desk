using VpsDesk.Domain.Servers;

namespace VpsDesk.Application.Abstractions;

public interface IRemoteLogService
{
    Task<IReadOnlyList<string>> ListSystemdServicesAsync(
        ServerProfile server,
        string? secret,
        CancellationToken cancellationToken = default);

    Task<string> ReadContainerLogsAsync(
        ServerProfile server,
        string? secret,
        string containerIdOrName,
        int tailLines,
        CancellationToken cancellationToken = default);

    Task<string> ReadSystemdLogsAsync(
        ServerProfile server,
        string? secret,
        string serviceName,
        int tailLines,
        CancellationToken cancellationToken = default);
}
