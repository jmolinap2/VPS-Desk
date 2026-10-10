using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VpsDesk.Desktop.ViewModels;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Desktop.Views;

public partial class MobileServersView : UserControl
{
    public MobileServersView()
    {
        InitializeComponent();
    }

    private void OnServerCardClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ServerProfile profile } && DataContext is MainWindowViewModel vm)
        {
            // Seleccionar carga el editor (OnSelectedServerInListChanged) antes de mostrar la hoja.
            vm.SelectedServerInList = profile;
            SetSheetOpen(true);
        }
    }

    private void OnNewServerClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        vm.NewServerCommand.Execute(null);
        SetSheetOpen(true);
    }

    private void OnCloseSheetClick(object? sender, RoutedEventArgs e) => SetSheetOpen(false);

    private void OnScrimPointerPressed(object? sender, PointerPressedEventArgs e) => SetSheetOpen(false);

    // Probar y guardar mantienen la hoja abierta para ver el resultado en el mensaje.
    private void OnActionClick(object? sender, RoutedEventArgs e)
    {
    }

    // Usar el servidor navega al panel; eliminar deja sin selección. En ambos casos la hoja se cierra.
    private void OnActivateClick(object? sender, RoutedEventArgs e) => SetSheetOpen(false);

    private void OnDeleteClick(object? sender, RoutedEventArgs e) => SetSheetOpen(false);

    private void SetSheetOpen(bool isOpen) => MobileSheet.SetOpen(Scrim, Sheet, isOpen);
}
