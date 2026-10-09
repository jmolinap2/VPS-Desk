using Avalonia.Controls;
using Avalonia.Input;
using VpsDesk.Desktop.ViewModels;

namespace VpsDesk.Desktop.Views;

public partial class MobileContainersView : UserControl
{
    public MobileContainersView()
    {
        InitializeComponent();
    }

    private void OnScrimPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ContainerModule.SelectedContainer = null;
        }
    }
}
