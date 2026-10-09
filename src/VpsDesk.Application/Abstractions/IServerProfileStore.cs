using VpsDesk.Domain.Servers;

namespace VpsDesk.Application.Abstractions;

public sealed record ServerProfileStoreSnapshot(
    IReadOnlyList<ServerProfile> Servers,
    Guid? SelectedServerId);

public interface IServerProfileStore
{
    Task<ServerProfileStoreSnapshot> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(
        IReadOnlyCollection<ServerProfile> servers,
        Guid? selectedServerId,
        CancellationToken cancellationToken = default);
}
