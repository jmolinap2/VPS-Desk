using Avalonia.Controls;
using Avalonia.Interactivity;
using VpsDesk.Desktop.Services;
using VpsDesk.Desktop.ViewModels;

namespace VpsDesk.Desktop.Views;

public partial class ActivityLogView : UserControl
{
    public ActivityLogView() => InitializeComponent();

    // Los eventos se siguen registrando mientras la pantalla está cerrada: al abrirla se actualiza.
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        (DataContext as ActivityLogViewModel)?.Refresh();
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ActivityLogViewModel model) await DeploymentReportActions.CopyTextAsync(this, model.BuildReport());
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ActivityLogViewModel model)
            await DeploymentReportActions.SaveTextAsync(this, model.BuildReport(), ActivityReport.SuggestFileName());
    }
}
