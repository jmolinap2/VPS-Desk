using System.Text.Json;
using VpsDesk.Application.Runtime;

namespace VpsDesk.Desktop.Services;

/// <summary>
/// Non-sensitive deployment settings remembered for a specific server/project workspace.
/// Secrets remain in the server session and are never written by this store.
/// </summary>
public sealed record DeploymentProfile(
    Guid ServerId,
    string RemoteRepositoryPath,
    string Branch,
    string ComposeFile,
    bool RequireEnvironmentFile,
    string EnvironmentFileName,
    bool PullImages,
    bool BuildImages,
    bool? CleanBuildCache = null,
    string? DeploymentTargetId = null,
    string? MigrationModeId = null,
    bool? RunMigrations = null,
    bool? MigrationOnly = null,
    Guid? ProjectId = null);

public sealed record DeploymentProfileStoreSnapshot(IReadOnlyList<DeploymentProfile> Profiles);

public sealed class DeploymentProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public DeploymentProfileStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(VpsDeskDataPaths.RoamingRoot, "deployments.json");
    }

    public DeploymentProfile? Find(Guid serverId)
    {
        var profiles = Load().Profiles;
        return profiles.FirstOrDefault(profile => profile.ServerId == serverId && profile.ProjectId is null)
               ?? profiles.FirstOrDefault(profile => profile.ServerId == serverId);
    }

    public DeploymentProfile? Find(Guid serverId, Guid projectId)
        => Load().Profiles.FirstOrDefault(profile => profile.ServerId == serverId && profile.ProjectId == projectId);

    public IReadOnlyList<DeploymentProfile> List(Guid serverId)
        => Load().Profiles.Where(profile => profile.ServerId == serverId).ToArray();

    public void Upsert(DeploymentProfile profile)
    {
        var profiles = Load().Profiles
            .Where(existing => !SameIdentity(existing, profile))
            .Append(profile)
            .ToArray();

        Save(profiles);
    }

    /// <summary>
    /// Converts the old server-only deployment record into a project-scoped record without
    /// dropping any previously saved branch, Compose, target or migration preferences.
    /// </summary>
    public void PromoteLegacyProfile(Guid serverId, string remoteRepositoryPath, Guid projectId)
    {
        var snapshot = Load();
        var normalizedPath = NormalizePath(remoteRepositoryPath);
        var legacy = snapshot.Profiles.FirstOrDefault(profile =>
            profile.ServerId == serverId
            && profile.ProjectId is null
            && NormalizePath(profile.RemoteRepositoryPath).Equals(normalizedPath, StringComparison.Ordinal));
        if (legacy is null) return;

        var promoted = legacy with { ProjectId = projectId };
        var profiles = snapshot.Profiles
            .Where(profile => !ReferenceEquals(profile, legacy)
                              && !(profile.ServerId == serverId && profile.ProjectId == projectId))
            .Append(promoted)
            .ToArray();
        Save(profiles);
    }

    public void Remove(Guid serverId)
    {
        var profiles = Load().Profiles
            .Where(profile => profile.ServerId != serverId)
            .ToArray();

        Save(profiles);
    }

    public void RemoveProject(Guid serverId, Guid projectId)
    {
        var profiles = Load().Profiles
            .Where(profile => !(profile.ServerId == serverId && profile.ProjectId == projectId))
            .ToArray();
        Save(profiles);
    }

    private static bool SameIdentity(DeploymentProfile left, DeploymentProfile right)
    {
        if (left.ServerId != right.ServerId) return false;
        if (left.ProjectId is not null || right.ProjectId is not null)
        {
            return left.ProjectId == right.ProjectId;
        }

        return NormalizePath(left.RemoteRepositoryPath)
            .Equals(NormalizePath(right.RemoteRepositoryPath), StringComparison.Ordinal);
    }

    private DeploymentProfileStoreSnapshot Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new DeploymentProfileStoreSnapshot([]);
            }

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<DeploymentProfileStoreSnapshot>(json, JsonOptions)
                   ?? new DeploymentProfileStoreSnapshot([]);
        }
        catch
        {
            return new DeploymentProfileStoreSnapshot([]);
        }
    }

    private void Save(IReadOnlyCollection<DeploymentProfile> profiles)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(new DeploymentProfileStoreSnapshot(profiles.ToArray()), JsonOptions);
        var temporaryPath = _filePath + ".tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, _filePath, overwrite: true);
    }

    private static string NormalizePath(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length > 1) trimmed = trimmed.TrimEnd('/');
        return trimmed;
    }
}
