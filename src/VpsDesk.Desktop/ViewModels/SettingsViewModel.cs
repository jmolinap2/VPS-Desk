using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Desktop.Services;

namespace VpsDesk.Desktop.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettingsStore _store;
    private readonly Action<string> _applyLanguage;
    private bool _suppressPersistence;

    public IReadOnlyList<string> LanguageOptions { get; } = ["Sistema", "Español", "English"];
    public IReadOnlyList<int> RefreshIntervalOptions { get; } = [5, 10, 15, 30, 60];
    public IReadOnlyList<int> TerminalScrollbackOptions { get; } = [1000, 2500, 5000, 10000, 20000];

    [ObservableProperty] private string _selectedLanguage = "Sistema";
    [ObservableProperty] private int _refreshIntervalSeconds = 10;
    [ObservableProperty] private bool _backupBeforeRemoteSave = true;
    [ObservableProperty] private int _terminalFontSize = 14;
    [ObservableProperty] private int _terminalScrollbackLines = 5000;
    [ObservableProperty] private bool _confirmMultilineTerminalPaste = true;
    [ObservableProperty] private string _statusMessage = "Las preferencias se guardan localmente y no contienen credenciales.";

    public string SettingsPath => _store.SettingsPath;
    public string DataDirectory => Path.GetDirectoryName(_store.SettingsPath) ?? string.Empty;
    public VpsDeskSettings CurrentSettings => ToSettings();

    public event EventHandler? PreferencesChanged;

    public SettingsViewModel(
        AppSettingsStore store,
        VpsDeskSettings settings,
        Action<string> applyLanguage)
    {
        _store = store;
        _applyLanguage = applyLanguage;
        ApplySnapshot(settings);
    }

    partial void OnSelectedLanguageChanged(string value) => Persist(languageChanged: true);
    partial void OnRefreshIntervalSecondsChanged(int value) => Persist();
    partial void OnBackupBeforeRemoteSaveChanged(bool value) => Persist();
    partial void OnTerminalFontSizeChanged(int value) => Persist();
    partial void OnTerminalScrollbackLinesChanged(int value) => Persist();
    partial void OnConfirmMultilineTerminalPasteChanged(bool value) => Persist();

    [RelayCommand]
    private void ResetDefaults()
    {
        ApplySnapshot(VpsDeskSettings.Default);
        Persist(languageChanged: true);
        StatusMessage = "Preferencias restablecidas a los valores predeterminados.";
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = DataDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"No se pudo abrir la carpeta local: {ex.Message}";
        }
    }

    private void ApplySnapshot(VpsDeskSettings settings)
    {
        _suppressPersistence = true;
        SelectedLanguage = settings.Language switch
        {
            "es-ES" => "Español",
            "en-US" => "English",
            _ => "Sistema"
        };
        RefreshIntervalSeconds = settings.RefreshIntervalSeconds;
        BackupBeforeRemoteSave = settings.BackupBeforeRemoteSave;
        TerminalFontSize = settings.TerminalFontSize;
        TerminalScrollbackLines = settings.TerminalScrollbackLines;
        ConfirmMultilineTerminalPaste = settings.ConfirmMultilineTerminalPaste;
        _suppressPersistence = false;
    }

    private void Persist(bool languageChanged = false)
    {
        if (_suppressPersistence) return;

        try
        {
            var settings = ToSettings();
            _store.Save(settings);
            if (languageChanged) _applyLanguage(settings.Language);
            StatusMessage = "Preferencias guardadas localmente.";
            PreferencesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusMessage = $"No se pudieron guardar las preferencias: {ex.Message}";
        }
    }

    private VpsDeskSettings ToSettings()
        => new(
            SelectedLanguage switch
            {
                "Español" => "es-ES",
                "English" => "en-US",
                _ => "system"
            },
            RefreshIntervalSeconds,
            BackupBeforeRemoteSave,
            TerminalFontSize,
            TerminalScrollbackLines,
            ConfirmMultilineTerminalPaste);
}
