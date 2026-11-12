using Avalonia.Controls;
using Avalonia.Input;
using VpsDesk.Desktop.ViewModels;

namespace VpsDesk.Desktop.Views;

public partial class DeploymentModals : UserControl
{
    public DeploymentModals()
    {
        InitializeComponent();
    }

    // Tocar el fondo (no la tarjeta) cierra la capa superior. Un prevuelo en curso se cancela y
    // una operación en segundo plano solo se oculta: ningún modal deja al usuario sin salida.
    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, sender)) return;
        (DataContext as DeploymentsViewModel)?.TryDismissTopLayer();
    }
}
