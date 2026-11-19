using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace VpsDesk.Desktop.Views;

public partial class MobileFileEditor : UserControl
{
    public MobileFileEditor()
    {
        InitializeComponent();
        Focusable = true;
        // Mientras se escribe, la cabecera y los avisos ceden su espacio al editor: con el teclado
        // abierto en un teléfono, lo que se edita casi no cabía en pantalla.
        AddHandler(GotFocusEvent, OnFocusChanged, RoutingStrategies.Bubble);
        AddHandler(LostFocusEvent, OnFocusChanged, RoutingStrategies.Bubble);
    }

    private void OnFocusChanged(object? sender, RoutedEventArgs e)
        => Dispatcher.UIThread.Post(RefreshEditingMode, DispatcherPriority.Background);

    private void RefreshEditingMode()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        var editing = focused is TextBox box && this.IsVisible && IsDescendant(box);
        EditorHeader.IsVisible = !editing;
        EditorNotices.IsVisible = !editing;
        DoneButton.IsVisible = editing;
    }

    private bool IsDescendant(Avalonia.Visual visual)
    {
        for (Avalonia.Visual? current = visual; current is not null; current = current.GetVisualParent())
            if (ReferenceEquals(current, this)) return true;
        return false;
    }

    private void OnDoneClick(object? sender, RoutedEventArgs e)
    {
        Focus();   // quita el foco al campo de texto: se cierra el teclado
        RefreshEditingMode();
    }

    private void OnCopyFile(object? sender, RoutedEventArgs e)
        => EnvironmentClipboardActions.CopyFile(sender);

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, sender))
            (DataContext as VpsDesk.Desktop.ViewModels.FilesViewModel)?.CancelSaveCommand.Execute(null);
    }
}
