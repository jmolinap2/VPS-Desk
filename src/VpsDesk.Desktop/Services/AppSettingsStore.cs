using System.Text.Json;

namespace VpsDesk.Desktop.Services;

public sealed record VpsDeskSettings(
    string Language,
    int RefreshIntervalSeconds,
    bool BackupBeforeRemoteSave,
    int TerminalFontSize,
    int TerminalScrollbackLines,
    bool ConfirmMultilineTerminalPaste)
{
    public static VpsDeskSettings Default { get; } = new(
        "system",
        10,
        true,
        14,
        5000,
        true);
}

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VPS Desk",
        "settings.json");

    public VpsDeskSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return VpsDeskSettings.Default;
            var json = File.ReadAllText(SettingsPath);
            var loaded = JsonSerializer.Deserialize<VpsDeskSettings>(json, JsonOptions);
            return Sanitize(loaded ?? VpsDeskSettings.Default);
        }
        catch
        {
            return VpsDeskSettings.Default;
        }
    }

    public void Save(VpsDeskSettings settings)
    {
        var sanitized = Sanitize(settings);
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(sanitized, JsonOptions));
    }

    private static VpsDeskSettings Sanitize(VpsDeskSettings settings)
    {
        var language = settings.Language is "es-ES" or "en-US" or "system"
            ? settings.Language
            : "system";
        var refresh = settings.RefreshIntervalSeconds is 5 or 10 or 15 or 30 or 60
            ? settings.RefreshIntervalSeconds
            : 10;
        var font = Math.Clamp(settings.TerminalFontSize, 11, 22);
        var scrollback = settings.TerminalScrollbackLines is 1000 or 2500 or 5000 or 10000 or 20000
            ? settings.TerminalScrollbackLines
            : 5000;

        return settings with
        {
            Language = language,
            RefreshIntervalSeconds = refresh,
            TerminalFontSize = font,
            TerminalScrollbackLines = scrollback
        };
    }
}
