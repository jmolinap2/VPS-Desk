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

    private void OnMenuButtonClick(object? sender, RoutedEventArgs e) => SetDrawerOpen(!Drawer.IsVisible);

    private void OnScrimPointerPressed(object? sender, PointerPressedEventArgs e) => SetDrawerOpen(false);

    private void OnNavItemClick(object? sender, RoutedEventArgs e) => SetDrawerOpen(false);

    private void SetDrawerOpen(bool isOpen)
    {
        Drawer.IsVisible = isOpen;
        Scrim.IsVisible = isOpen;
    }
}
