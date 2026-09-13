namespace VpsDesk.Domain.Files;

public sealed record RemoteFileEntry(
    string Name,
    string FullPath,
    bool IsDirectory,
    long SizeBytes,
    DateTimeOffset LastWriteTimeUtc)
{
    private static readonly HashSet<string> ScriptExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".sh", ".bash", ".zsh", ".ps1", ".py", ".rb", ".pl" };

    private static readonly HashSet<string> ConfigExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".json", ".xml", ".yml", ".yaml", ".ini", ".conf", ".config", ".toml", ".cfg", ".service", ".env" };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico" };

    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".zip", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".7z", ".rar" };

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".dll", ".so", ".bin", ".o", ".obj", ".class", ".jar", ".pdf", ".iso" };

    public string Kind => IsDirectory ? "Folder" : "File";

    /// <summary>
    /// Drives which icon and which contextual actions (Ver/Editar) a row can offer.
    /// Files with no recognized extension (many Linux config files, e.g. hosts, fstab)
    /// default to Text since the app has always been able to read them as UTF-8.
    /// </summary>
    public RemoteFileCategory Category
    {
        get
        {
            if (IsDirectory) return RemoteFileCategory.Directory;

            var extension = Path.GetExtension(Name);
            if (ImageExtensions.Contains(extension)) return RemoteFileCategory.Image;
            if (ArchiveExtensions.Contains(extension)) return RemoteFileCategory.Archive;
            if (BinaryExtensions.Contains(extension)) return RemoteFileCategory.Binary;
            if (ScriptExtensions.Contains(extension)) return RemoteFileCategory.Script;
            if (ConfigExtensions.Contains(extension)) return RemoteFileCategory.Config;
            return RemoteFileCategory.Text;
        }
    }

    public string CategoryLabel => Category switch
    {
        RemoteFileCategory.Directory => "Folder",
        RemoteFileCategory.Script => "Script",
        RemoteFileCategory.Config => "Config",
        RemoteFileCategory.Image => "Image",
        RemoteFileCategory.Archive => "Archive",
        RemoteFileCategory.Binary => "Binary",
        _ => "Text"
    };

    public bool IsScriptFile => Category == RemoteFileCategory.Script;
    public bool IsConfigFile => Category == RemoteFileCategory.Config;
    public bool IsImageFile => Category == RemoteFileCategory.Image;
    public bool IsArchiveFile => Category == RemoteFileCategory.Archive;
    public bool IsBinaryFile => Category == RemoteFileCategory.Binary;
    public bool IsPlainTextFile => Category == RemoteFileCategory.Text;

    public bool CanEdit => Category is RemoteFileCategory.Text or RemoteFileCategory.Config or RemoteFileCategory.Script;
    public bool CanPreview => CanEdit || Category == RemoteFileCategory.Image;
    public bool HasNoPreview => Category is RemoteFileCategory.Archive or RemoteFileCategory.Binary;

    public bool IsSensitive
    {
        get
        {
            if (IsDirectory) return false;
            return Name.Equals(".env", StringComparison.OrdinalIgnoreCase)
                   || Name.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)
                   || Name.Contains("secret", StringComparison.OrdinalIgnoreCase)
                   || Name.EndsWith(".pem", StringComparison.OrdinalIgnoreCase)
                   || Name.EndsWith(".key", StringComparison.OrdinalIgnoreCase)
                   || Name.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase);
        }
    }

    public string SizeLabel
    {
        get
        {
            if (IsDirectory) return "--";
            if (SizeBytes <= 0) return "0 B";
            string[] units = ["B", "KB", "MB", "GB", "TB"];
            var value = (double)SizeBytes;
            var index = 0;
            while (value >= 1024 && index < units.Length - 1)
            {
                value /= 1024;
                index++;
            }

            return $"{value:F1} {units[index]}";
        }
    }
}
