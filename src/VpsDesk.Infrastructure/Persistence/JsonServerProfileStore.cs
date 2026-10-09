using System.Text.Json;
using System.Text.Json.Serialization;
using VpsDesk.Application.Abstractions;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Infrastructure.Persistence;

public sealed class JsonServerProfileStore : IServerProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath;

    public JsonServerProfileStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    public async Task<ServerProfileStoreSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return new ServerProfileStoreSnapshot([], null);
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<ServerProfileStoreSnapshot>(stream, JsonOptions, cancellationToken)
                   ?? new ServerProfileStoreSnapshot([], null);
        }
        catch (JsonException)
        {
            return new ServerProfileStoreSnapshot([], null);
        }
        catch (IOException)
        {
            return new ServerProfileStoreSnapshot([], null);
        }
        catch (UnauthorizedAccessException)
        {
            return new ServerProfileStoreSnapshot([], null);
        }
    }

    public async Task SaveAsync(
        IReadOnlyCollection<ServerProfile> servers,
        Guid? selectedServerId,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var snapshot = new ServerProfileStoreSnapshot(servers.ToArray(), selectedServerId);
        var tempPath = _filePath + ".tmp";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken);
        }

        File.Move(tempPath, _filePath, true);
    }
}
