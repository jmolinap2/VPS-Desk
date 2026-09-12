using VpsDesk.Domain.Servers;
using VpsDesk.Domain.Storage;

namespace VpsDesk.Application.Abstractions;

public interface IStorageService
{
    Task<StorageSnapshot> ReadAsync(
        ServerProfile server,
        string? secret,
        CancellationToken cancellationToken = default);

    Task<StorageCleanupResult> CleanupAsync(
        ServerProfile server,
        StorageCleanupRequest request,
        string? secret,
        CancellationToken cancellationToken = default);
}
