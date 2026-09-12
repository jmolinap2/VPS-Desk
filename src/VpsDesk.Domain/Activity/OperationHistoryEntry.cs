namespace VpsDesk.Domain.Activity;

public enum OperationKind
{
    Deployment,
    ContainerAction,
    SecurityAudit,
    FileOperation,
    Other
}

public enum OperationOutcome
{
    Success,
    Warning,
    Failed
}

public sealed record OperationHistoryEntry(
    Guid Id,
    Guid ServerId,
    string ServerName,
    string Environment,
    OperationKind Kind,
    string Action,
    string Summary,
    OperationOutcome Outcome,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    string? RemoteRepositoryPath = null,
    string? Branch = null,
    string? PreviousCommit = null,
    string? DeployedCommit = null,
    string? ComposeFile = null,
    string? FailedStep = null,
    string? Output = null,
    string? Details = null)
{
    public TimeSpan Duration => FinishedAt - StartedAt;
    public bool IsDeployment => Kind == OperationKind.Deployment;
}
