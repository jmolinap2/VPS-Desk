using VpsDesk.Domain.Security;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Application.Abstractions;

public interface ISecurityAuditService
{
    Task<SecurityAuditSnapshot> AuditAsync(
        ServerProfile server,
        string? secret,
        CancellationToken cancellationToken = default);
}
