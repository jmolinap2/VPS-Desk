namespace VpsDesk.Domain.Containers;

public sealed record DockerContainerInfo(
    string Id,
    string Name,
    string Image,
    string State,
    string Status,
    string Ports,
    string? ComposeProject,
    string Health,
    double CpuPercent,
    string MemoryUsage,
    double MemoryPercent,
    string NetworkIo)
{
    public bool IsRunning => string.Equals(State, "running", StringComparison.OrdinalIgnoreCase);
    public bool IsUnhealthy => string.Equals(Health, "unhealthy", StringComparison.OrdinalIgnoreCase);
}

public enum DockerContainerAction
{
    Start,
    Stop,
    Restart
}

public sealed record DockerContainerActionResult(
    DockerContainerAction Action,
    string ContainerId,
    bool Succeeded,
    string Output,
    string Error);
