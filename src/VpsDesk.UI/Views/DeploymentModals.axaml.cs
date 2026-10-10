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

    private void OnPreflightPointerEntered(object? sender, PointerEventArgs e)
        => (DataContext as DeploymentsViewModel)?.SetPreflightPointerOver(true);

    private void OnPreflightPointerExited(object? sender, PointerEventArgs e)
        => (DataContext as DeploymentsViewModel)?.SetPreflightPointerOver(false);

    private void OnPreflightPointerPressed(object? sender, PointerPressedEventArgs e)
        => (DataContext as DeploymentsViewModel)?.PinPreflightCountdown();
}
