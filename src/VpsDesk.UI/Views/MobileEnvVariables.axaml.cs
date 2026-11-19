using Avalonia.Controls;

namespace VpsDesk.Desktop.Views;

public partial class MobileEnvVariables : UserControl
{
    public MobileEnvVariables()
    {
        InitializeComponent();
    }
    private void OnCopyVariable(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => EnvironmentClipboardActions.CopyVariable(sender);
}
