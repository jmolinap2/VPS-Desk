namespace VpsDesk.Android;

internal static class AndroidAppPaths
{
    public static string DataRoot
    {
        get
        {
            var filesDir = global::Android.App.Application.Context.FilesDir?.AbsolutePath;
            if (string.IsNullOrWhiteSpace(filesDir))
            {
                throw new InvalidOperationException("Android internal files directory is unavailable.");
            }

            return filesDir;
        }
    }

    public static string ServerProfilesFile => Path.Combine(DataRoot, "servers.json");
}
