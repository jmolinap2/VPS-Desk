using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Interactivity;
using VpsDesk.Desktop.ViewModels;

namespace VpsDesk.Desktop.Views;

public partial class MobileShell : UserControl
{
    private readonly ShellTelemetryController _telemetry = new(refreshOnActivation: true);
    private IInputPane? _inputPane;
    private TopLevel? _topLevel;

    public MobileShell()
    {
        InitializeComponent();

        // Sin esto los medidores del Panel nunca se refrescaban en el móvil:
        // el refresco inicial y el temporizador solo existían en MainShell.
        Loaded += async (_, _) =>
        {
            AttachInputPane();
            AttachBackHandler();
            await _telemetry.StartAsync(DataContext);
        };
        Unloaded += async (_, _) =>
        {
            DetachInputPane();
            DetachBackHandler();
            await _telemetry.StopAsync();
        };
    }

    // El teclado virtual de Android se dibuja encima de la app. Para que lo que se escribe
    // siga visible, todo el contenido se acorta por la parte de abajo lo que mide el teclado:
    // las hojas y los formularios quedan por encima y las listas se vuelven desplazables.
    private void AttachInputPane()
    {
        DetachInputPane();
        _inputPane = TopLevel.GetTopLevel(this)?.InputPane;
        if (_inputPane is null) return;

        _inputPane.StateChanged += OnInputPaneStateChanged;
        ApplyKeyboardInset(_inputPane.State == InputPaneState.Open ? _inputPane.OccludedRect.Height : 0);
    }

    private void DetachInputPane()
    {
        if (_inputPane is not null) _inputPane.StateChanged -= OnInputPaneStateChanged;
        _inputPane = null;
    }

    private void OnInputPaneStateChanged(object? sender, InputPaneStateEventArgs e)
        => ApplyKeyboardInset(e.NewState == InputPaneState.Open ? e.EndRect.Height : 0);

    private void ApplyKeyboardInset(double height)
        => Padding = new Thickness(0, 0, 0, Math.Max(0, height));

    // Botón/gesto «atrás» de Android: cierra lo último que se abrió antes de salir de la app.
    // Orden: cajón → hoja o modal abierto → volver al Panel. Solo desde el Panel, sin nada
    // abierto, el sistema cierra la aplicación (comportamiento normal de Android).
    private void AttachBackHandler()
    {
        DetachBackHandler();
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null) _topLevel.BackRequested += OnBackRequested;
    }

    private void DetachBackHandler()
    {
        if (_topLevel is not null) _topLevel.BackRequested -= OnBackRequested;
        _topLevel = null;
    }

    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (Drawer.Classes.Contains("open"))
        {
            SetDrawerOpen(false);
            e.Handled = true;
            return;
        }

        if (DataContext is not MainWindowViewModel vm) return;

        // Capa abierta en el módulo de la pantalla activa, o una hoja con estado en code-behind.
        if (vm.TryDismissTopLayer() || MobileSheet.TryDismissTop())
        {
            e.Handled = true;
            return;
        }

        if (!vm.IsDashboardPage)
        {
            vm.NavigateCommand.Execute("Dashboard");
            e.Handled = true;
        }
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
