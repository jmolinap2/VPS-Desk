using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VpsDesk.Desktop.Views;

public partial class MobileLogsView : UserControl
{
    public MobileLogsView()
    {
        InitializeComponent();
    }

    // La tarjeta de fuente despliega sus campos justo debajo; el estado es solo de presentación.
    private void OnSourceHeaderClick(object? sender, RoutedEventArgs e) => SetFieldsOpen(!SourceFields.IsVisible);

    // Al cargar, los campos se pliegan para dejar la pantalla al visor.
    private void OnLoadClick(object? sender, RoutedEventArgs e) => SetFieldsOpen(false);

    private void SetFieldsOpen(bool open)
    {
        SourceFields.IsVisible = open;
        SourceChevronDown.IsVisible = !open;
        SourceChevronUp.IsVisible = open;
        // Con los campos abiertos, el botón rápido sobra: «Cargar» está dentro de la tarjeta.
        QuickLoadButton.IsVisible = !open;
        if (open) SourceFields.BringIntoView();
    }
}
