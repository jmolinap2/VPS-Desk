using VpsDesk.Domain.Servers;

namespace VpsDesk.Desktop.Services;

public sealed record BootstrapServerContext(
    ServerProfile? Profile,
    string? Secret,
    string? RemoteRepositoryPath = null,
    string? ComposeFile = null,
    string? Warning = null);

public static class BootstrapServerProfileLoader
{
    public static BootstrapServerContext Load()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>())
        {
            if (pair.Key is string key && pair.Value is string value)
            {
                values[key] = value;
            }
        }

        var envPath = FindEnvFile();
        if (envPath != null)
        {
            foreach (var line in File.ReadLines(envPath))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
                var index = trimmed.IndexOf('=');
                if (index <= 0) continue;
                var key = trimmed[..index].Trim();
                var value = trimmed[(index + 1)..].Trim().Trim('"');
                if (!values.ContainsKey(key)) values[key] = value;
            }
        }

        if (!values.TryGetValue("SERVER_HOST", out var host) || string.IsNullOrWhiteSpace(host))
        {
            return new BootstrapServerContext(
                null,
                null,
                Warning: "Configure a server from the Servers screen or provide SERVER_HOST for bootstrap testing.");
        }

        values.TryGetValue("SERVER_USER", out var user);
        values.TryGetValue("SSH_KEY_PATH", out var keyPath);
        values.TryGetValue("SSH_PASSWORD", out var password);
        values.TryGetValue("SSH_HOST_FINGERPRINT", out var hostFingerprint);
        values.TryGetValue("SERVER_PROVIDER", out var provider);
        values.TryGetValue("REMOTE_REPO_PATH", out var remoteRepositoryPath);
        values.TryGetValue("COMPOSE_FILE", out var composeFile);

        if (!string.IsNullOrWhiteSpace(keyPath)
            && !Path.IsPathRooted(keyPath)
            && envPath is not null)
        {
            keyPath = Path.GetFullPath(keyPath, Path.GetDirectoryName(envPath)!);
        }

        var port = values.TryGetValue("SSH_PORT", out var portRaw) && int.TryParse(portRaw, out var parsedPort)
            ? parsedPort
            : 22;

        var authType = !string.IsNullOrWhiteSpace(keyPath)
            ? SshAuthenticationType.PrivateKey
            : SshAuthenticationType.Password;

        var profile = new ServerProfile(
            Guid.Parse("8c680213-98ad-4a3c-a637-f39517c2a62a"),
            values.TryGetValue("SERVER_NAME", out var name) && !string.IsNullOrWhiteSpace(name) ? name : host,
            host,
            port,
            string.IsNullOrWhiteSpace(user) ? "root" : user,
            authType,
            string.IsNullOrWhiteSpace(keyPath) ? null : keyPath,
            null,
            string.IsNullOrWhiteSpace(provider) ? "Hostinger" : provider,
            ServerEnvironment.Production,
            ["bootstrap"],
            string.IsNullOrWhiteSpace(hostFingerprint) ? null : hostFingerprint);

        return new BootstrapServerContext(
            profile,
            string.IsNullOrWhiteSpace(password) ? null : password,
            string.IsNullOrWhiteSpace(remoteRepositoryPath) ? null : remoteRepositoryPath,
            string.IsNullOrWhiteSpace(composeFile) ? null : composeFile);
    }

    private static string? FindEnvFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, ".env");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        return null;
    }
}
