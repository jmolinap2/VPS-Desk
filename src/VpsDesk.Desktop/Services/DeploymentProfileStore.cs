using System.Text.Json;

namespace VpsDesk.Desktop.Services;

/// <summary>
/// Non-sensitive deployment settings remembered for a specific server profile.
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
    bool BuildImages);

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
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VPS Desk",
            "deployments.json");
    }

    public DeploymentProfile? Find(Guid serverId)
        => Load().Profiles.FirstOrDefault(profile => profile.ServerId == serverId);

    public void Upsert(DeploymentProfile profile)
    {
        var profiles = Load().Profiles
            .Where(existing => existing.ServerId != profile.ServerId)
            .Append(profile)
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
}
