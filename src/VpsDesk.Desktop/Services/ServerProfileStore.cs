using System.Text.Json;
using System.Text.Json.Serialization;
using VpsDesk.Application.Runtime;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Desktop.Services;

public sealed record ServerProfileStoreSnapshot(
    IReadOnlyList<ServerProfile> Servers,
    Guid? SelectedServerId);

public sealed class ServerProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath;

    public ServerProfileStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(VpsDeskDataPaths.RoamingRoot, "servers.json");
    }

    public ServerProfileStoreSnapshot Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new ServerProfileStoreSnapshot([], null);
            }

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<ServerProfileStoreSnapshot>(json, JsonOptions)
                   ?? new ServerProfileStoreSnapshot([], null);
        }
        catch
        {
            return new ServerProfileStoreSnapshot([], null);
        }
    }

    public void Save(IReadOnlyCollection<ServerProfile> servers, Guid? selectedServerId)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var snapshot = new ServerProfileStoreSnapshot(servers.ToArray(), selectedServerId);
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        File.WriteAllText(_filePath, json);
    }
}
