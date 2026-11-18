using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VpsDesk.Desktop.ViewModels;

/// <summary>
/// Cronómetro del pase en curso. La barra de progreso solo avanza al terminar cada paso y un paso
/// (p. ej. construir las imágenes) puede tardar minutos: el tiempo corriendo cada segundo es la prueba
/// de que la aplicación sigue viva y esperando al servidor.
/// </summary>
public partial class DeploymentsViewModel
{
    private DispatcherTimer? _elapsedTimer;

    [ObservableProperty] private string _deploymentElapsedText = "00:00";

    private void UpdateElapsedTimer(bool running)
    {
        if (!running)
        {
            _elapsedTimer?.Stop();
            return;
        }

        if (_elapsedTimer is null)
        {
            _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _elapsedTimer.Tick += (_, _) => RefreshElapsed();
        }

        RefreshElapsed();
        _elapsedTimer.Start();
    }

    private void RefreshElapsed()
    {
        var started = _observedDeploymentStartedAt == default ? DateTimeOffset.UtcNow : _observedDeploymentStartedAt;
        var elapsed = DateTimeOffset.UtcNow - started;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        DeploymentElapsedText = elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }
}
