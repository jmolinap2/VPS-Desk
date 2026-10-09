namespace VpsDesk.Android;

internal static class AndroidRuntimeSecrets
{
    private static readonly object Sync = new();
    private static readonly Dictionary<Guid, string> Passwords = [];

    public static void Set(Guid serverId, string? secret)
    {
        lock (Sync)
        {
            if (string.IsNullOrEmpty(secret))
            {
                Passwords.Remove(serverId);
                return;
            }

            Passwords[serverId] = secret;
        }
    }

    public static string? Get(Guid serverId)
    {
        lock (Sync)
        {
            return Passwords.TryGetValue(serverId, out var secret) ? secret : null;
        }
    }

    public static void Remove(Guid serverId)
    {
        lock (Sync)
        {
            Passwords.Remove(serverId);
        }
    }
}
