namespace VpsDesk.Domain.Security;

public enum SecurityCheckSeverity
{
    Info,
    Passed,
    Warning,
    Critical
}

public sealed record SecurityCheck(
    string Code,
    string Label,
    SecurityCheckSeverity Severity,
    string Detail);

public sealed record SecurityAuditSnapshot(
    IReadOnlyList<SecurityCheck> Checks,
    DateTimeOffset CheckedAtUtc)
{
    public int PassedCount => Checks.Count(x => x.Severity == SecurityCheckSeverity.Passed);
    public int WarningCount => Checks.Count(x => x.Severity == SecurityCheckSeverity.Warning);
    public int CriticalCount => Checks.Count(x => x.Severity == SecurityCheckSeverity.Critical);
}
