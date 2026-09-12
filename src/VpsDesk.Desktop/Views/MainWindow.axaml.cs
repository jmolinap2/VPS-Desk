using Avalonia.Controls;
using Avalonia.Threading;
using VpsDesk.Desktop.ViewModels;

namespace VpsDesk.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _telemetryTimer = new()
    {
        Interval = TimeSpan.FromSeconds(10)
    };

    public MainWindow()
    {
        InitializeComponent();

        _telemetryTimer.Tick += async (_, _) =>
        {
            if (DataContext is not MainWindowViewModel vm) return;

            if (vm.IsDashboardPage
                && vm.AutoRefreshEnabled
                && vm.RefreshCommand.CanExecute(null))
            {
                await vm.RefreshCommand.ExecuteAsync(null);
            }
            else if (vm.IsContainersPage
                     && vm.ContainerModule.AutoRefreshEnabled
                     && vm.ContainerModule.RefreshCommand.CanExecute(null))
            {
                await vm.ContainerModule.RefreshCommand.ExecuteAsync(null);
            }
        };

        Opened += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm && vm.RefreshCommand.CanExecute(null))
            {
                await vm.RefreshCommand.ExecuteAsync(null);
            }

            _telemetryTimer.Start();
        };

        Closed += (_, _) => _telemetryTimer.Stop();
    }
}
