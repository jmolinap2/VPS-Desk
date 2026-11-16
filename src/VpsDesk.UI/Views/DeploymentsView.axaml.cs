using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VpsDesk.Desktop.Views;

public partial class DeploymentsView : UserControl
{
    public DeploymentsView()
    {
        InitializeComponent();
    }

    private void OnReportCopyClick(object? sender, RoutedEventArgs e) => DeploymentReportActions.OnCopyClick(sender, e);

    private void OnReportSaveClick(object? sender, RoutedEventArgs e) => DeploymentReportActions.OnSaveClick(sender, e);
}
