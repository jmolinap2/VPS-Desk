namespace VpsDesk.Domain.Servers;

public enum ServerCapability
{
    Ssh,
    Linux,
    Bash,
    Sftp,
    Systemd,
    Journalctl,
    Docker,
    DockerCompose,
    Git,
    Nginx
}

public enum CompatibilityLevel
{
    Unknown,
    Certified,
    Compatible,
    Partial,
    Unsupported
}

public sealed record CapabilityResult(
    ServerCapability Capability,
    bool Available,
    string? Version = null,
    string? Detail = null);

public sealed record CompatibilitySnapshot(
    Guid ServerId,
    CompatibilityLevel Level,
    string? Distribution,
    string? Kernel,
    IReadOnlyCollection<CapabilityResult> Capabilities,
    DateTimeOffset CheckedAt);
