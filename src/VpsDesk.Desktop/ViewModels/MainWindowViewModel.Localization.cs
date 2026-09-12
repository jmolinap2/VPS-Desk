using VpsDesk.Desktop.Localization;

namespace VpsDesk.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    public string LocalizedHeaderTitle => LocalizationService.T($"Nav_{SelectedPage}");
    public string LocalizedHeaderSubtitle => LocalizationService.T($"Header_{SelectedPage}");

    partial void OnHeaderTitleChanged(string value)
        => OnPropertyChanged(nameof(LocalizedHeaderTitle));

    partial void OnHeaderSubtitleChanged(string value)
        => OnPropertyChanged(nameof(LocalizedHeaderSubtitle));
}
