using VpsDesk.Domain.Containers;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Application.Abstractions;

public interface IContainerService
{
    Task<IReadOnlyList<DockerContainerInfo>> ListAsync(
        ServerProfile server,
        string? secret,
        CancellationToken cancellationToken = default);

    Task<DockerContainerActionResult> ExecuteAsync(
        ServerProfile server,
        string? secret,
        string containerIdOrName,
        DockerContainerAction action,
        CancellationToken cancellationToken = default);
}
