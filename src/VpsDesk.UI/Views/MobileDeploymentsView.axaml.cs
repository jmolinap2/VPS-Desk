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

    // Pestañas Salida / Pasos: solo estado de presentación, por eso viven acá
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

    // El historial se abre por toque explícito (no por selección): el ViewModel autoselecciona
    // la primera entrada al cargar y eso abriría la hoja sola al entrar a la pestaña.
    // El estado de la hoja vive en el ViewModel (IsHistorySheetOpen) para que «atrás» la cierre.
    private void OnHistoryEntryTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: OperationHistoryEntry entry }
            && DataContext is DeploymentsViewModel vm)
        {
            vm.OpenHistoryEntryCommand.Execute(entry);
        }
    }

    private void OnHistoryScrimPressed(object? sender, PointerPressedEventArgs e)
        => (DataContext as DeploymentsViewModel)?.CloseHistorySheetCommand.Execute(null);

    // La hoja de opciones también es estado del ViewModel; tocar el fondo la cierra.
    private void OnOptionsScrimPressed(object? sender, PointerPressedEventArgs e)
        => (DataContext as DeploymentsViewModel)?.CloseAdvancedOptionsCommand.Execute(null);
}
