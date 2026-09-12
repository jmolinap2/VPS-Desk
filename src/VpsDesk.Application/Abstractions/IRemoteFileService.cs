using VpsDesk.Domain.Files;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Application.Abstractions;

public interface IRemoteFileService
{
    Task<IReadOnlyList<RemoteFileEntry>> ListAsync(
        ServerProfile server,
        string remotePath,
        string? secret,
        CancellationToken cancellationToken = default);

    Task<string?> ReadTextAsync(
        ServerProfile server,
        string remotePath,
        string? secret,
        CancellationToken cancellationToken = default);

    Task WriteTextAsync(
        ServerProfile server,
        string remotePath,
        string content,
        string? secret,
        CancellationToken cancellationToken = default);
}
