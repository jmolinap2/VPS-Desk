using Avalonia.Controls;

namespace VpsDesk.Desktop.Views;

public partial class FilesView : UserControl
{
    public FilesView()
    {
        InitializeComponent();
    }
    private void OnCopyVariable(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => EnvironmentClipboardActions.CopyVariable(sender);
}
