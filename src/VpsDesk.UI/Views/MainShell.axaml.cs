using Avalonia.Controls;

namespace VpsDesk.Desktop.Views;

/// <summary>
/// Shell de escritorio (sidebar fija + panel de contenido). La telemetría y el
/// ciclo de vida viven en <see cref="ShellTelemetryController"/>, compartido
/// con MobileShell. Usa Loaded/Unloaded (no Window.Opened/Closed) para
/// funcionar igual dentro de una Window o como raíz de vista única.
/// </summary>
public partial class MainShell : UserControl
{
    private readonly ShellTelemetryController _telemetry = new();

    public MainShell()
    {
        InitializeComponent();

        Loaded += async (_, _) => await _telemetry.StartAsync(DataContext);
        Unloaded += async (_, _) => await _telemetry.StopAsync();
    }
}
