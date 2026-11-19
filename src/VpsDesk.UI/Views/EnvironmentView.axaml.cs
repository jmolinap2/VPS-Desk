using Avalonia.Controls;

namespace VpsDesk.Desktop.Views;

public partial class EnvironmentView : UserControl
{
    public EnvironmentView()
    {
        InitializeComponent();
    }
    private void OnCopyVariable(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => EnvironmentClipboardActions.CopyVariable(sender);
    private void OnCopyFile(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => EnvironmentClipboardActions.CopyFile(sender);
}
