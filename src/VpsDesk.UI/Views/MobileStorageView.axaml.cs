using Avalonia.Controls;

namespace VpsDesk.Desktop.Views;

public partial class MobileStorageView : UserControl
{
    public MobileStorageView()
    {
        InitializeComponent();
    }
    private void OnScrimPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, sender))
            (DataContext as VpsDesk.Desktop.ViewModels.StorageViewModel)?.CancelCleanupCommand.Execute(null);
    }
}
