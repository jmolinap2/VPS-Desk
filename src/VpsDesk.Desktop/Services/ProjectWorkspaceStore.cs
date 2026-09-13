using System.Text.Json;

namespace VpsDesk.Desktop.Services;

/// <summary>
/// A project is the user-facing association between one VPS and one remote Git/Compose workspace.
/// No credentials or .env values are persisted here.
/// </summary>
public sealed record ProjectWorkspace(
    Guid Id,
    Guid ServerId,
    string Name,
    string RemoteRepositoryPath,
    string Branch,
    string ComposeFile,
    string EnvironmentFileName = ".env",
    bool HasRecipe = false,
    string? RecipeFileName = null,
    int TargetCount = 0,
    bool HasMigrations = false,
    DateTimeOffset? LastSeenAt = null)
{
    public string ModeLabel => HasRecipe ? "Integración VPS Desk" : "Modo estándar Docker Compose";
    public string CapabilitySummary => HasRecipe
        ? $"{TargetCount} objetivo(s)" + (HasMigrations ? " · Migraciones" : string.Empty)
        : "Git · Docker Compose";
}

public sealed record ProjectWorkspaceStoreSnapshot(
    IReadOnlyList<ProjectWorkspace> Projects,
    Dictionary<Guid, Guid>? SelectedProjectByServer = null);

public sealed class ProjectWorkspaceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public ProjectWorkspaceStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VPS Desk",
            "projects.json");
    }

    public IReadOnlyList<ProjectWorkspace> List(Guid serverId)
        => Load().Projects
            .Where(project => project.ServerId == serverId)
            .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(project => project.RemoteRepositoryPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public ProjectWorkspace? Find(Guid projectId)
        => Load().Projects.FirstOrDefault(project => project.Id == projectId);

    public ProjectWorkspace? FindByPath(Guid serverId, string remoteRepositoryPath)
    {
        var normalized = NormalizePath(remoteRepositoryPath);
        return Load().Projects.FirstOrDefault(project =>
            project.ServerId == serverId
            && NormalizePath(project.RemoteRepositoryPath).Equals(normalized, StringComparison.Ordinal));
    }

    public ProjectWorkspace? GetSelected(Guid serverId)
    {
        var snapshot = Load();
        if (snapshot.SelectedProjectByServer is not null
            && snapshot.SelectedProjectByServer.TryGetValue(serverId, out var selectedId))
        {
            return snapshot.Projects.FirstOrDefault(project => project.Id == selectedId && project.ServerId == serverId);
        }

        return snapshot.Projects.FirstOrDefault(project => project.ServerId == serverId);
    }

    public void Upsert(ProjectWorkspace project, bool select = false)
    {
        var snapshot = Load();
        var normalizedPath = NormalizePath(project.RemoteRepositoryPath);
        var existing = snapshot.Projects.FirstOrDefault(item =>
            item.Id == project.Id
            || (item.ServerId == project.ServerId
                && NormalizePath(item.RemoteRepositoryPath).Equals(normalizedPath, StringComparison.Ordinal)));

        var normalizedProject = project with
        {
            Id = existing?.Id ?? project.Id,
            RemoteRepositoryPath = normalizedPath,
            LastSeenAt = project.LastSeenAt ?? DateTimeOffset.UtcNow
        };

        var projects = snapshot.Projects
            .Where(item => item.Id != normalizedProject.Id
                           && !(item.ServerId == normalizedProject.ServerId
                                && NormalizePath(item.RemoteRepositoryPath).Equals(normalizedPath, StringComparison.Ordinal)))
            .Append(normalizedProject)
            .ToArray();

        var selected = new Dictionary<Guid, Guid>(snapshot.SelectedProjectByServer ?? []);
        if (select) selected[normalizedProject.ServerId] = normalizedProject.Id;
        Save(new ProjectWorkspaceStoreSnapshot(projects, selected));
    }

    public void Select(Guid serverId, Guid projectId)
    {
        var snapshot = Load();
        if (!snapshot.Projects.Any(project => project.ServerId == serverId && project.Id == projectId)) return;

        var selected = new Dictionary<Guid, Guid>(snapshot.SelectedProjectByServer ?? [])
        {
            [serverId] = projectId
        };
        Save(snapshot with { SelectedProjectByServer = selected });
    }

    public void Remove(Guid projectId)
    {
        var snapshot = Load();
        var removed = snapshot.Projects.FirstOrDefault(project => project.Id == projectId);
        if (removed is null) return;

        var projects = snapshot.Projects.Where(project => project.Id != projectId).ToArray();
        var selected = new Dictionary<Guid, Guid>(snapshot.SelectedProjectByServer ?? []);
        if (selected.TryGetValue(removed.ServerId, out var selectedId) && selectedId == projectId)
        {
            selected.Remove(removed.ServerId);
            var fallback = projects.FirstOrDefault(project => project.ServerId == removed.ServerId);
            if (fallback is not null) selected[removed.ServerId] = fallback.Id;
        }
        Save(new ProjectWorkspaceStoreSnapshot(projects, selected));
    }

    public void RemoveForServer(Guid serverId)
    {
        var snapshot = Load();
        var projects = snapshot.Projects.Where(project => project.ServerId != serverId).ToArray();
        var selected = new Dictionary<Guid, Guid>(snapshot.SelectedProjectByServer ?? []);
        selected.Remove(serverId);
        Save(new ProjectWorkspaceStoreSnapshot(projects, selected));
    }

    public ProjectWorkspace? ImportLegacyProfile(DeploymentProfile? profile)
    {
        if (profile is null || string.IsNullOrWhiteSpace(profile.RemoteRepositoryPath)) return null;

        var existing = FindByPath(profile.ServerId, profile.RemoteRepositoryPath);
        if (existing is not null) return existing;

        var path = NormalizePath(profile.RemoteRepositoryPath);
        var name = path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "Proyecto";
        var project = new ProjectWorkspace(
            Guid.NewGuid(),
            profile.ServerId,
            name,
            path,
            string.IsNullOrWhiteSpace(profile.Branch) ? "main" : profile.Branch,
            string.IsNullOrWhiteSpace(profile.ComposeFile) ? "docker-compose.yml" : profile.ComposeFile,
            string.IsNullOrWhiteSpace(profile.EnvironmentFileName) ? ".env" : profile.EnvironmentFileName,
            LastSeenAt: DateTimeOffset.UtcNow);
        Upsert(project, select: true);
        return project;
    }

    private ProjectWorkspaceStoreSnapshot Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return new ProjectWorkspaceStoreSnapshot([], []);
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<ProjectWorkspaceStoreSnapshot>(json, JsonOptions)
                   ?? new ProjectWorkspaceStoreSnapshot([], []);
        }
        catch
        {
            return new ProjectWorkspaceStoreSnapshot([], []);
        }
    }

    private void Save(ProjectWorkspaceStoreSnapshot snapshot)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var temporaryPath = _filePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot, JsonOptions));
        File.Move(temporaryPath, _filePath, overwrite: true);
    }

    private static string NormalizePath(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length > 1) trimmed = trimmed.TrimEnd('/');
        return trimmed;
    }
}
