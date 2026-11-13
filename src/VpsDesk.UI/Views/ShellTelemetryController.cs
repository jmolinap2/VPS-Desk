using System.ComponentModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using VpsDesk.Application.Runtime;
using VpsDesk.Desktop.ViewModels;

namespace VpsDesk.Desktop.Views;

/// <summary>
/// Ciclo de vida de la telemetría compartido por todos los shells
/// (MainShell en escritorio, MobileShell en Android/iOS): refresco inicial al
/// mostrarse, temporizador de auto-refresco y limpieza de la terminal al
/// descargarse. Vivía duplicado dentro de MainShell; al faltar en MobileShell
/// los medidores del Panel se quedaban en 0 % y sin tiempo de actividad.
/// </summary>
internal sealed class ShellTelemetryController
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly bool _refreshOnActivation;
    private MainWindowViewModel? _viewModel;
    private IActivatableLifetime? _activatable;

    /// <param name="refreshOnActivation">
    /// En móvil la app se suspende y Android corta su red con la pantalla
    /// apagada; si un refresco falla ahí, el auto-refresco queda desactivado y
    /// nunca se recupera solo. Con esto vuelve a consultar cada vez que la app
    /// regresa a primer plano. En escritorio queda apagado para no lanzar una
    /// consulta SSH en cada cambio de ventana.
    /// </param>
    public ShellTelemetryController(bool refreshOnActivation = false)
    {
        _refreshOnActivation = refreshOnActivation;
        _timer.Tick += async (_, _) => await OnTickAsync();
    }

    public async Task StartAsync(object? dataContext)
    {
        if (dataContext is not MainWindowViewModel vm) return;

        _viewModel = vm;
        ApplyInterval(vm.SettingsModule.RefreshIntervalSeconds);
        vm.SettingsModule.PropertyChanged += SettingsOnPropertyChanged;

        if (vm.RefreshCommand.CanExecute(null))
        {
            await vm.RefreshCommand.ExecuteAsync(null);
        }

        _timer.Start();

        if (_refreshOnActivation && _activatable is null)
        {
            _activatable = Avalonia.Application.Current?.TryGetFeature<IActivatableLifetime>();
            if (_activatable is not null)
            {
                _activatable.Activated += OnActivated;
                _activatable.Deactivated += OnDeactivated;
            }
        }
    }

    public async Task StopAsync()
    {
        _timer.Stop();
        if (_activatable is not null)
        {
            _activatable.Activated -= OnActivated;
            _activatable.Deactivated -= OnDeactivated;
            _activatable = null;
        }

        if (_viewModel is null) return;

        _viewModel.SettingsModule.PropertyChanged -= SettingsOnPropertyChanged;
        await _viewModel.DisposeTerminalAsync();
        _viewModel = null;
    }

    // Con la app en segundo plano o la pantalla apagada Android corta la red: seguir sondeando solo
    // produce fallos y reconexiones inútiles (y gasta batería y datos). Se pausa el temporizador y
    // al volver a primer plano se reanuda con una consulta inmediata.
    private void OnDeactivated(object? sender, ActivatedEventArgs e)
        => Dispatcher.UIThread.Post(() => _timer.Stop());

    private void OnActivated(object? sender, ActivatedEventArgs e)
        => Dispatcher.UIThread.Post(async () =>
        {
            if (_viewModel is not { } vm) return;

            ApplyInterval(vm.SettingsModule.RefreshIntervalSeconds);
            _timer.Start();

            // El sondeo periódico ya se bloquea solo mientras hay uno en curso y tolera fallos
            // puntuales: justo al volver la red puede tardar unos segundos en estar lista.
            await vm.RefreshPeriodicAsync();
        });

    private async Task OnTickAsync()
    {
        if (_viewModel is not { } vm) return;

        if (vm.IsDashboardPage
            && vm.AutoRefreshEnabled
            && vm.RefreshCommand.CanExecute(null))
        {
            await vm.RefreshPeriodicAsync();
        }
        else if (vm.IsContainersPage
                 && vm.ContainerModule.AutoRefreshEnabled
                 && vm.ContainerModule.RefreshCommand.CanExecute(null))
        {
            await vm.ContainerModule.RefreshCommand.ExecuteAsync(null);
        }
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.RefreshIntervalSeconds)
            && sender is SettingsViewModel settings)
        {
            ApplyInterval(settings.RefreshIntervalSeconds);
        }
    }

    // Con red medida (datos móviles) no se sondea más de una vez cada 30 s, aunque el ajuste sea menor.
    private void ApplyInterval(int seconds)
    {
        var clamped = Math.Clamp(seconds, 5, 60);
        if (DataSaverPolicy.IsMeteredSafe()) clamped = Math.Max(clamped, 30);
        _timer.Interval = TimeSpan.FromSeconds(clamped);
    }
}
