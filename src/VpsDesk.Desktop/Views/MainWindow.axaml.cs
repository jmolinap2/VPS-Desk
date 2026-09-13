using System.ComponentModel;
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
    private MainWindowViewModel? _viewModel;

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
            if (DataContext is not MainWindowViewModel vm) return;

            _viewModel = vm;
            ApplyTelemetryInterval(vm.SettingsModule.RefreshIntervalSeconds);
            vm.SettingsModule.PropertyChanged += SettingsOnPropertyChanged;

            if (vm.RefreshCommand.CanExecute(null))
            {
                await vm.RefreshCommand.ExecuteAsync(null);
            }

            _telemetryTimer.Start();
        };

        Closed += async (_, _) =>
        {
            _telemetryTimer.Stop();
            if (_viewModel != null)
            {
                _viewModel.SettingsModule.PropertyChanged -= SettingsOnPropertyChanged;
                await _viewModel.DisposeTerminalAsync();
            }
        };
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.RefreshIntervalSeconds)
            && sender is SettingsViewModel settings)
        {
            ApplyTelemetryInterval(settings.RefreshIntervalSeconds);
        }
    }

    private void ApplyTelemetryInterval(int seconds)
        => _telemetryTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(seconds, 5, 60));
}
