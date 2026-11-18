using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VpsDesk.Desktop.ViewModels;
using VpsDesk.Domain.Containers;

namespace VpsDesk.Desktop.Views;

public partial class MobileContainersView : UserControl
{
    public MobileContainersView()
    {
        InitializeComponent();
    }

    private void OnContainerTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: DockerContainerInfo container } && DataContext is MainWindowViewModel vm)
        {
            vm.ContainerModule.SelectedContainer = container;
            vm.ContainerModule.IsDetailOpen = true;
            e.Handled = true;
        }
    }

    private void OnScrimPointerPressed(object? sender, PointerPressedEventArgs e) => CloseDetail();

    private void OnCloseSheetClick(object? sender, RoutedEventArgs e) => CloseDetail();

    // La hoja está abierta mientras haya un contenedor seleccionado; cerrarla es quitar la selección.
    private void CloseDetail()
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ContainerModule.IsDetailOpen = false;
            vm.ContainerModule.SelectedContainer = null;
        }
    }
}
