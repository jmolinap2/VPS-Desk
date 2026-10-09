using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VpsDesk.Desktop.Views;

public partial class MobileProjectsView : UserControl
{
    public MobileProjectsView()
    {
        InitializeComponent();
    }

    // Asociados / Descubiertos: estado puramente visual (qué lista se ve y sus
    // acciones), por eso vive en la vista y no en el ViewModel.
    private void OnTabClick(object? sender, RoutedEventArgs e)
    {
        var showDiscovered = sender is Button { Tag: "discovered" };

        AssociatedList.IsVisible = !showDiscovered;
        DiscoveredList.IsVisible = showDiscovered;
        AssociatedActions.IsVisible = !showDiscovered;
        DiscoveredActions.IsVisible = showDiscovered;

        SetActive(AssociatedTab, !showDiscovered);
        SetActive(DiscoveredTab, showDiscovered);
    }

    private static void SetActive(Button button, bool active)
    {
        if (active) button.Classes.Add("active");
        else button.Classes.Remove("active");
    }
}
