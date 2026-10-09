using VpsDesk.Desktop.Services;

namespace VpsDesk.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private SettingsViewModel? _settingsModule;

    public SettingsViewModel SettingsModule
        => _settingsModule ?? throw new InvalidOperationException("Settings module has not been initialized.");

    public void InitializeSettings(
        AppSettingsStore store,
        VpsDeskSettings settings,
        Action<string> applyLanguage)
    {
        if (_settingsModule != null) return;

        _settingsModule = new SettingsViewModel(store, settings, applyLanguage);
        _settingsModule.PreferencesChanged += (_, _) => ApplyPreferencesToModules();
        ApplyPreferencesToModules();
        OnPropertyChanged(nameof(SettingsModule));
    }

    private void ApplyPreferencesToModules()
    {
        if (_settingsModule == null) return;
        var settings = _settingsModule.CurrentSettings;

        if (_filesModule != null)
        {
            _filesModule.CreateBackupBeforeSave = settings.BackupBeforeRemoteSave;
        }

        _terminalModule?.ApplyPreferences(
            settings.TerminalFontSize,
            settings.TerminalScrollbackLines,
            settings.ConfirmMultilineTerminalPaste);
    }
}
