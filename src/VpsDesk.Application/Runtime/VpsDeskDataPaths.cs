namespace VpsDesk.Application.Runtime;

/// <summary>
/// Resolves writable application data. A portable.mode marker next to the executable keeps
/// every file in a data folder beside the application so the whole directory can be moved.
/// </summary>
public static class VpsDeskDataPaths
{
    private const string PortableMarker = "portable.mode";

    public static bool IsPortable => File.Exists(Path.Combine(AppContext.BaseDirectory, PortableMarker));

    public static string RoamingRoot => ResolveRoot(Environment.SpecialFolder.ApplicationData, "VPS Desk");

    public static string LocalRoot => ResolveRoot(Environment.SpecialFolder.LocalApplicationData, "VPSDesk");

    /// <summary>
    /// Carpeta de las copias descargadas del VPS. VPSDESK_BACKUP_DIR la redefine;
    /// en modo portable o con VPSDESK_DATA_DIR vive junto a los datos; si no, en
    /// Documentos\VPS Desk\Backups para que el usuario la encuentre y la respalde.
    /// </summary>
    public static string BackupsRoot
    {
        get
        {
            var overridePath = Environment.GetEnvironmentVariable("VPSDESK_BACKUP_DIR");
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                return Path.GetFullPath(overridePath, AppContext.BaseDirectory);
            }

            var dataDirOverride = Environment.GetEnvironmentVariable("VPSDESK_DATA_DIR");
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return IsPortable || !string.IsNullOrWhiteSpace(dataDirOverride) || string.IsNullOrWhiteSpace(documents)
                ? Path.Combine(LocalRoot, "Backups")
                : Path.Combine(documents, "VPS Desk", "Backups");
        }
    }

    private static string ResolveRoot(Environment.SpecialFolder installedFolder, string installedDirectory)
    {
        var overridePath = Environment.GetEnvironmentVariable("VPSDESK_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath, AppContext.BaseDirectory);
        }

        return IsPortable
            ? Path.Combine(AppContext.BaseDirectory, "data")
            : Path.Combine(Environment.GetFolderPath(installedFolder), installedDirectory);
    }
}
