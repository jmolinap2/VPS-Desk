using VpsDesk.Desktop.Localization;

namespace VpsDesk.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private static bool IsSpanishUi
        => LocalizationService.Current.CurrentCulture.StartsWith("es", StringComparison.OrdinalIgnoreCase);

    public string LocalizedProjectsNavigationLabel => IsSpanishUi ? "Proyectos" : "Projects";

    public string LocalizedHeaderTitle => SelectedPage == "Projects"
        ? LocalizedProjectsNavigationLabel
        : LocalizationService.T($"Nav_{SelectedPage}");

    public string LocalizedHeaderSubtitle => SelectedPage == "Projects"
        ? IsSpanishUi
            ? "Asocia proyectos Git + Docker Compose al VPS activo"
            : "Associate Git + Docker Compose projects with the active VPS"
        : LocalizationService.T($"Header_{SelectedPage}");

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(LocalizedProjectsNavigationLabel));
        OnPropertyChanged(nameof(LocalizedHeaderTitle));
        OnPropertyChanged(nameof(LocalizedHeaderSubtitle));
    }

    partial void OnHeaderTitleChanged(string value)
        => OnPropertyChanged(nameof(LocalizedHeaderTitle));

    partial void OnHeaderSubtitleChanged(string value)
        => OnPropertyChanged(nameof(LocalizedHeaderSubtitle));
}
