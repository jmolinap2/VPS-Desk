using System.ComponentModel;

namespace VpsDesk.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    public bool IsTerminalPage => SelectedPage == "Terminal";
    public bool IsSettingsPage => SelectedPage == "Settings";

    public void InitializeNavigationState()
    {
        PropertyChanged += HandleNavigationStateChanged;
        OnPropertyChanged(nameof(IsTerminalPage));
        OnPropertyChanged(nameof(IsSettingsPage));
    }

    private void HandleNavigationStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SelectedPage)) return;

        OnPropertyChanged(nameof(IsTerminalPage));
        OnPropertyChanged(nameof(IsSettingsPage));
    }
}
