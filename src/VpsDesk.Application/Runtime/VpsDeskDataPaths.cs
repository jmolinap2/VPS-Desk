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
