using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace VpsDesk.Desktop.Views;

public partial class MobileShell : UserControl
{
    public MobileShell()
    {
        InitializeComponent();
    }

    private void OnMenuButtonClick(object? sender, RoutedEventArgs e) => SetDrawerOpen(!Drawer.Classes.Contains("open"));

    private void OnScrimPointerPressed(object? sender, PointerPressedEventArgs e) => SetDrawerOpen(false);

    private void OnNavItemClick(object? sender, RoutedEventArgs e) => SetDrawerOpen(false);

    private void SetDrawerOpen(bool isOpen)
    {
        // Las clases "open" disparan las Transitions declaradas en
        // MobileShell.axaml (scrim que se desvanece, cajón que se desliza)
        // en vez de un IsVisible que aparece/desaparece de golpe.
        if (isOpen)
        {
            Drawer.Classes.Add("open");
            Scrim.Classes.Add("open");
        }
        else
        {
            Drawer.Classes.Remove("open");
            Scrim.Classes.Remove("open");
        }
    }
}
