using Avalonia.Controls;
using VpsDesk.Desktop.ViewModels;

namespace VpsDesk.Desktop.Views;

public partial class BackupModals : UserControl
{
    public BackupModals() => InitializeComponent();

    // Tocar el fondo (no la tarjeta) cierra la capa superior, igual que «atrás» en el teléfono.
    private void OnModalScrimPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, sender)) return;
        (DataContext as DeploymentsViewModel)?.TryDismissTopLayer();
    }
}
