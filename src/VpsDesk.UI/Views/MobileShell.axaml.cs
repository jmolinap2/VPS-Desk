using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace VpsDesk.Desktop.Views;

public partial class MobileShell : UserControl
{
    private readonly ShellTelemetryController _telemetry = new(refreshOnActivation: true);

    public MobileShell()
    {
        InitializeComponent();

        // Sin esto los medidores del Panel nunca se refrescaban en el móvil:
        // el refresco inicial y el temporizador solo existían en MainShell.
        Loaded += async (_, _) => await _telemetry.StartAsync(DataContext);
        Unloaded += async (_, _) => await _telemetry.StopAsync();
    }

    private void OnMenuButtonClick(object? sender, RoutedEventArgs e) => SetDrawerOpen(!Drawer.Classes.Contains("open"));

    private void OnScrimPointerPressed(object? sender, PointerPressedEventArgs e) => SetDrawerOpen(false);

    private void OnNavItemClick(object? sender, RoutedEventArgs e) => SetDrawerOpen(false);

    private void SetDrawerOpen(bool isOpen)
    {
        // Las clases "open" disparan las Transitions declaradas en
        // MobileShell.axaml (scrim que se desvanece, cajón que se desliza)
        // en vez de un IsVisible que aparece/desaparece de golpe.
        if (isOpen)
        {
            Drawer.Classes.Add("open");
            Scrim.Classes.Add("open");
        }
        else
        {
            Drawer.Classes.Remove("open");
            Scrim.Classes.Remove("open");
        }
    }
}
