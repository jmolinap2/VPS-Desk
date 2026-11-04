using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VpsDesk.Desktop.Services;

namespace VpsDesk.Desktop.Views;

public partial class ToastHost : UserControl
{
    public ToastHost()
    {
        InitializeComponent();
        DataContext = NotificationCenter.Shared;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is AppNotification notification)
        {
            NotificationCenter.Shared.Dismiss(notification);
        }

        e.Handled = true;
    }

    // Un toque en el aviso lo descarta, salvo que el usuario esté seleccionando texto para copiarlo.
    private void OnToastPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is SelectableTextBlock) return;

        if ((sender as Control)?.DataContext is AppNotification notification)
        {
            NotificationCenter.Shared.Dismiss(notification);
        }
    }
}
