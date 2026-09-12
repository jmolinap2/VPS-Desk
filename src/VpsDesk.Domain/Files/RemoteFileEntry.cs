namespace VpsDesk.Domain.Files;

public sealed record RemoteFileEntry(
    string Name,
    string FullPath,
    bool IsDirectory,
    long SizeBytes,
    DateTimeOffset LastWriteTimeUtc)
{
    public string Kind => IsDirectory ? "Folder" : "File";

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
