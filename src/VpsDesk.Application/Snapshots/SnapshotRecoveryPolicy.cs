namespace VpsDesk.Application.Snapshots;

public static class SnapshotRecoveryPolicy
{
    public static bool IsSameAvailableSnapshot(VpsSnapshotInfo expected, VpsSnapshotInfo? current, DateTimeOffset now)
        => current is not null && current.Id == expected.Id && current.CreatedAt == expected.CreatedAt
            && (current.ExpiresAt is null || current.ExpiresAt > now);
}
