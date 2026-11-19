using Avalonia.Controls;

namespace VpsDesk.Desktop.Views;

public partial class MobileFileEditor : UserControl
{
    public MobileFileEditor()
    {
        InitializeComponent();
    }
    private void OnCopyFile(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => EnvironmentClipboardActions.CopyFile(sender);
    private void OnScrimPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, sender))
            (DataContext as VpsDesk.Desktop.ViewModels.FilesViewModel)?.CancelSaveCommand.Execute(null);
    }
}
