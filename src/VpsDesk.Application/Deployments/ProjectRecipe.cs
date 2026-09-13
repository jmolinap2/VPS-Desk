using VpsDesk.Domain.Servers;

namespace VpsDesk.Application.Deployments;

public enum ProjectRecipeStatus
{
    NotFound,
    Loaded,
    Invalid
}

public sealed record ProjectRecipeLoadResult(
    ProjectRecipeStatus Status,
    ProjectRecipe? Recipe = null,
    string? FileName = null,
    string? Error = null);

public sealed record ProjectRecipe(
    int Version,
    string Name,
    ProjectDeployRecipe Deploy,
    ProjectMigrationRecipe? Migrations = null);

public sealed record ProjectDeployRecipe(
    string? DefaultTarget,
    IReadOnlyList<ProjectDeploymentTarget> Targets);

public sealed record ProjectDeploymentTarget(
    string Id,
    string Label,
    IReadOnlyList<string> Services,
    bool RunMigrationsByDefault = true,
    bool IsRecipeDefined = true);

public sealed record ProjectMigrationRecipe(
    string Label,
    string? DefaultMode,
    IReadOnlyList<string> RequiredServices,
    IReadOnlyList<ProjectMigrationMode> Modes);

public sealed record ProjectMigrationMode(
    string Id,
    string Label,
    string Command,
    ProjectOperationInput? Input = null);

public sealed record ProjectOperationInput(
    string Label,
    string? Placeholder = null,
    bool Required = true);

public interface IProjectRecipeService
{
    Task<ProjectRecipeLoadResult> LoadAsync(
        ServerProfile server,
        string remoteRepositoryPath,
        string? secret,
        CancellationToken cancellationToken = default);
}
