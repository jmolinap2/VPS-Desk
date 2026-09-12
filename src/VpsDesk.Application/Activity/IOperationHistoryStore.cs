using VpsDesk.Domain.Activity;

namespace VpsDesk.Application.Activity;

public interface IOperationHistoryStore
{
    Task AddAsync(OperationHistoryEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OperationHistoryEntry>> GetRecentAsync(int limit = 20, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OperationHistoryEntry>> GetDeploymentsAsync(Guid? serverId = null, int limit = 100, CancellationToken cancellationToken = default);
}
