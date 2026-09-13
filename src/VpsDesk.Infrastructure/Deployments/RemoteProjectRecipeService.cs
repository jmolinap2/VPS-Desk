using System.Text.RegularExpressions;
using VpsDesk.Application.Abstractions;
using VpsDesk.Application.Deployments;
using VpsDesk.Application.Logging;
using VpsDesk.Domain.Servers;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace VpsDesk.Infrastructure.Deployments;

public sealed class RemoteProjectRecipeService(ISshCommandExecutor ssh) : IProjectRecipeService
{
    private const int MaxRecipeBytes = 128 * 1024;
    private const string FileMarker = "__VPSDESK_RECIPE_FILE__=";
    private static readonly Regex SafeId = new("^[A-Za-z0-9][A-Za-z0-9_.-]*$", RegexOptions.Compiled);

    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public async Task<ProjectRecipeLoadResult> LoadAsync(
        ServerProfile server,
        string remoteRepositoryPath,
        string? secret,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(remoteRepositoryPath))
        {
            return new ProjectRecipeLoadResult(ProjectRecipeStatus.NotFound);
        }

        var repo = DeploymentPreflightService.ShellQuote(remoteRepositoryPath.Trim().TrimEnd('/'));
        var command = $$"""
            REPO={{repo}};
            for FILE in .vpsdesk.yml .vpsdesk.yaml; do
              if [ -f "$REPO/$FILE" ]; then
                SIZE=$(wc -c < "$REPO/$FILE" 2>/dev/null || echo 999999999)
                if [ "$SIZE" -gt {{MaxRecipeBytes}} ]; then
                  echo "Recipe file exceeds {{MaxRecipeBytes}} bytes." >&2
                  exit 6
                fi
                printf '{{FileMarker}}%s\n' "$FILE"
                cat "$REPO/$FILE"
                exit 0
              fi
            done
            exit 4
            """;

        SshCommandResult result;
        try
        {
            result = await ssh.ExecuteAsync(
                new SshCommandRequest(server, command, TimeSpan.FromSeconds(12)),
                secret,
                cancellationToken);
        }
        catch (Exception ex)
        {
            return new ProjectRecipeLoadResult(
                ProjectRecipeStatus.Invalid,
                Error: $"No se pudo comprobar la receta del proyecto: {ex.Message}");
        }

        if (result.ExitCode == 4)
        {
            return new ProjectRecipeLoadResult(ProjectRecipeStatus.NotFound);
        }

        if (!result.Succeeded)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? "No se pudo leer la receta del proyecto."
                : LogSanitizer.Sanitize(result.StandardError).Trim();
            return new ProjectRecipeLoadResult(ProjectRecipeStatus.Invalid, Error: detail);
        }

        var normalized = result.StandardOutput.Replace("\r\n", "\n", StringComparison.Ordinal);
        var firstBreak = normalized.IndexOf('\n');
        if (firstBreak <= 0 || !normalized.StartsWith(FileMarker, StringComparison.Ordinal))
        {
            return new ProjectRecipeLoadResult(
                ProjectRecipeStatus.Invalid,
                Error: "La respuesta remota no contiene una receta VPS Desk válida.");
        }

        var fileName = normalized[FileMarker.Length..firstBreak].Trim();
        var yaml = normalized[(firstBreak + 1)..];
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return new ProjectRecipeLoadResult(
                ProjectRecipeStatus.Invalid,
                FileName: fileName,
                Error: $"{fileName} está vacío.");
        }

        try
        {
            var dto = _deserializer.Deserialize<RecipeDto>(yaml)
                      ?? throw new InvalidOperationException("El YAML no contiene un documento.");
            var recipe = ValidateAndMap(dto);
            return new ProjectRecipeLoadResult(ProjectRecipeStatus.Loaded, recipe, fileName);
        }
        catch (Exception ex)
        {
            return new ProjectRecipeLoadResult(
                ProjectRecipeStatus.Invalid,
                FileName: fileName,
                Error: $"{fileName} no es válido: {ex.Message}");
        }
    }

    private static ProjectRecipe ValidateAndMap(RecipeDto dto)
    {
        if (dto.Version != 1)
        {
            throw new InvalidOperationException("Solo se soporta version: 1.");
        }

        var name = dto.Project?.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("project.name es obligatorio.");
        }

        var targetDtos = dto.Deploy?.Targets ?? [];
        var targets = targetDtos.Select(MapTarget).ToArray();
        EnsureUnique(targets.Select(x => x.Id), "deploy.targets");

        var defaultTarget = dto.Deploy?.DefaultTarget?.Trim();
        if (!string.IsNullOrWhiteSpace(defaultTarget)
            && targets.All(x => !x.Id.Equals(defaultTarget, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"deploy.defaultTarget '{defaultTarget}' no existe en deploy.targets.");
        }

        ProjectMigrationRecipe? migrations = null;
        if (dto.Migrations is not null)
        {
            var modes = (dto.Migrations.Modes ?? []).Select(MapMigrationMode).ToArray();
            if (modes.Length == 0)
            {
                throw new InvalidOperationException("migrations.modes debe declarar al menos un modo.");
            }
            EnsureUnique(modes.Select(x => x.Id), "migrations.modes");

            var defaultMode = dto.Migrations.DefaultMode?.Trim();
            if (!string.IsNullOrWhiteSpace(defaultMode)
                && modes.All(x => !x.Id.Equals(defaultMode, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"migrations.defaultMode '{defaultMode}' no existe en migrations.modes.");
            }

            var requiredServices = NormalizeServices(dto.Migrations.RequiredServices ?? []);
            migrations = new ProjectMigrationRecipe(
                string.IsNullOrWhiteSpace(dto.Migrations.Label)
                    ? "Migraciones de base de datos"
                    : dto.Migrations.Label.Trim(),
                defaultMode,
                requiredServices,
                modes);
        }

        return new ProjectRecipe(
            dto.Version,
            name,
            new ProjectDeployRecipe(defaultTarget, targets),
            migrations);
    }

    private static ProjectDeploymentTarget MapTarget(TargetDto dto)
    {
        var id = RequireSafeId(dto.Id, "deploy.targets[].id");
        var label = string.IsNullOrWhiteSpace(dto.Label) ? id : dto.Label.Trim();
        var services = NormalizeServices(dto.Services ?? []);
        if (services.Count == 0)
        {
            throw new InvalidOperationException($"El target '{id}' debe declarar al menos un servicio.");
        }

        return new ProjectDeploymentTarget(
            id,
            label,
            services,
            dto.MigrationsDefault ?? true,
            true);
    }

    private static ProjectMigrationMode MapMigrationMode(MigrationModeDto dto)
    {
        var id = RequireSafeId(dto.Id, "migrations.modes[].id");
        var label = string.IsNullOrWhiteSpace(dto.Label) ? id : dto.Label.Trim();
        var command = dto.Command?.Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new InvalidOperationException($"El modo de migración '{id}' debe declarar command.");
        }

        ProjectOperationInput? input = null;
        if (dto.Input is not null)
        {
            var inputLabel = dto.Input.Label?.Trim();
            if (string.IsNullOrWhiteSpace(inputLabel))
            {
                throw new InvalidOperationException($"El modo '{id}' declara input pero no input.label.");
            }
            if (!command.Contains("{{input}}", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"El modo '{id}' declara input pero command no contiene {{input}}.");
            }

            input = new ProjectOperationInput(
                inputLabel,
                dto.Input.Placeholder?.Trim(),
                dto.Input.Required ?? true);
        }
        else if (command.Contains("{{input}}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"El modo '{id}' usa {{input}} pero no declara input.");
        }

        return new ProjectMigrationMode(id, label, command, input);
    }

    private static IReadOnlyList<string> NormalizeServices(IEnumerable<string> values)
    {
        var result = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var service in result)
        {
            if (!SafeId.IsMatch(service))
            {
                throw new InvalidOperationException($"Nombre de servicio Compose no válido: '{service}'.");
            }
        }

        return result;
    }

    private static string RequireSafeId(string? value, string field)
    {
        var id = value?.Trim();
        if (string.IsNullOrWhiteSpace(id) || !SafeId.IsMatch(id))
        {
            throw new InvalidOperationException($"{field} debe usar letras, números, punto, guion o guion bajo.");
        }
        return id;
    }

    private static void EnsureUnique(IEnumerable<string> ids, string field)
    {
        var duplicate = ids
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"{field} contiene el identificador duplicado '{duplicate}'.");
        }
    }

    private sealed class RecipeDto
    {
        public int Version { get; set; }
        public ProjectDto? Project { get; set; }
        public DeployDto? Deploy { get; set; }
        public MigrationsDto? Migrations { get; set; }
    }

    private sealed class ProjectDto
    {
        public string? Name { get; set; }
    }

    private sealed class DeployDto
    {
        public string? DefaultTarget { get; set; }
        public List<TargetDto>? Targets { get; set; }
    }

    private sealed class TargetDto
    {
        public string? Id { get; set; }
        public string? Label { get; set; }
        public List<string>? Services { get; set; }
        public bool? MigrationsDefault { get; set; }
    }

    private sealed class MigrationsDto
    {
        public string? Label { get; set; }
        public string? DefaultMode { get; set; }
        public List<string>? RequiredServices { get; set; }
        public List<MigrationModeDto>? Modes { get; set; }
    }

    private sealed class MigrationModeDto
    {
        public string? Id { get; set; }
        public string? Label { get; set; }
        public string? Command { get; set; }
        public OperationInputDto? Input { get; set; }
    }

    private sealed class OperationInputDto
    {
        public string? Label { get; set; }
        public string? Placeholder { get; set; }
        public bool? Required { get; set; }
    }
}
