using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using VpsDesk.Desktop.ViewModels;
using VpsDesk.Domain.Activity;

namespace VpsDesk.Desktop.Views;

public partial class MobileDeploymentsView : UserControl
{
    private DeploymentsViewModel? _model;

    public MobileDeploymentsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachModel();
    }

    // Al abrir la configuración desde la tarjeta de destino, aparece con una animación corta y la
    // pantalla se desplaza hasta ella: se ve qué cambió, no solo crece la barra de desplazamiento.
    private void AttachModel()
    {
        if (_model is not null) _model.PropertyChanged -= OnModelPropertyChanged;
        _model = DataContext as DeploymentsViewModel;
        if (_model is not null) _model.PropertyChanged += OnModelPropertyChanged;
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DeploymentsViewModel.IsDeploymentConfigurationExpanded)
            || _model is not { IsDeploymentConfigurationExpanded: true }) return;

        ConfigPanel.Classes.Add("enter");
        DispatcherTimer.RunOnce(() =>
        {
            ConfigPanel.Classes.Remove("enter");
            // Solo el arranque de los campos: así la tarjeta de destino sigue a la vista.
            ConfigPanel.BringIntoView(new Avalonia.Rect(0, 0, ConfigPanel.Bounds.Width, 140));
        }, TimeSpan.FromMilliseconds(30));
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
