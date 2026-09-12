using VpsDesk.Domain.Servers;

namespace VpsDesk.Application.Abstractions;

public interface IRemoteFileService
{
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
