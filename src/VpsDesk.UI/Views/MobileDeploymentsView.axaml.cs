using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VpsDesk.Desktop.ViewModels;
using VpsDesk.Domain.Activity;

namespace VpsDesk.Desktop.Views;

public partial class MobileDeploymentsView : UserControl
{
    public MobileDeploymentsView()
    {
        InitializeComponent();
    }

    // Pestañas Salida / Proceso: solo estado de presentación, por eso viven acá
    // y no en el ViewModel. Cada una usa todo el ancho en vez de repartirse
    // dos columnas fijas como en escritorio.
    private void OnExecutionTabClick(object? sender, RoutedEventArgs e)
    {
        var showSteps = sender is Button { Tag: "steps" };

        OutputPanel.IsVisible = !showSteps;
        StepsPanel.IsVisible = showSteps;
        SetActive(OutputTabButton, !showSteps);
        SetActive(StepsTabButton, showSteps);
    }

    private static void SetActive(Button button, bool active)
    {
        if (active) button.Classes.Add("active");
        else button.Classes.Remove("active");
    }

    // El historial se abre por toque explícito (no por selección): el
    // ViewModel autoselecciona la primera entrada al cargar y eso abriría la
    // hoja sola al entrar a la pestaña.
    private void OnHistoryEntryTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: OperationHistoryEntry entry }
            && DataContext is DeploymentsViewModel vm)
        {
            vm.SelectedHistoryEntry = entry;
            SetHistorySheetOpen(true);
        }
    }

    private void OnHistoryScrimPressed(object? sender, PointerPressedEventArgs e) => SetHistorySheetOpen(false);

    // La hoja de opciones es estado del ViewModel (IsAdvancedOptionsOpen); tocar el fondo la cierra.
    private void OnOptionsScrimPressed(object? sender, PointerPressedEventArgs e)
        => (DataContext as DeploymentsViewModel)?.CloseAdvancedOptionsCommand.Execute(null);

    private void OnHistoryCloseClick(object? sender, RoutedEventArgs e) => SetHistorySheetOpen(false);

    private void SetHistorySheetOpen(bool isOpen)
    {
        if (isOpen)
        {
            HistorySheet.Classes.Add("open");
            HistoryScrim.Classes.Add("open");
        }
        else
        {
            HistorySheet.Classes.Remove("open");
            HistoryScrim.Classes.Remove("open");
        }
    }
}
