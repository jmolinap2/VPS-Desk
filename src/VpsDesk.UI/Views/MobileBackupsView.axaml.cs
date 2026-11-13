using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VpsDesk.Application.AppBackups;
using VpsDesk.Desktop.ViewModels;

namespace VpsDesk.Desktop.Views;

public partial class MobileBackupsView : UserControl
{
    public MobileBackupsView() => InitializeComponent();

    // Servidor / Este dispositivo: solo presentación, por eso vive acá y no en el ViewModel.
    private void OnTabClick(object? sender, RoutedEventArgs e)
    {
        var device = sender is Button { Tag: "device" };
        ServerPanel.IsVisible = !device;
        DevicePanel.IsVisible = device;
        SetActive(ServerTab, !device);
        SetActive(DeviceTab, device);
    }

    private static void SetActive(Button button, bool active)
    {
        if (active) button.Classes.Add("active");
        else button.Classes.Remove("active");
    }

    // Secciones plegables (configuración, zona de peligro): el botón nombra el panel en Tag.
    private void OnSectionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name } || this.FindControl<Control>(name) is not { } body) return;
        body.IsVisible = !body.IsVisible;
        var chevron = this.FindControl<Avalonia.Controls.Shapes.Path>(name == "SettingsBody" ? "SettingsChevron" : "DangerChevron");
        if (chevron is not null) chevron.RenderTransform = body.IsVisible ? Avalonia.Media.Transformation.TransformOperations.Parse("rotate(180deg)") : null;
    }

    // La copia se abre por toque explícito: el estado de la hoja vive en el ViewModel para que «atrás» la cierre.
    private void OnBackupTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: AppBackupInfo backup } && DataContext is DeploymentsViewModel vm)
            vm.OpenBackupSheetCommand.Execute(backup);
    }

    private void OnBackupScrimPressed(object? sender, PointerPressedEventArgs e)
        => (DataContext as DeploymentsViewModel)?.CloseBackupSheetCommand.Execute(null);

    private async void OnExportClick(object? sender, RoutedEventArgs e)
        => await BackupsView.ExportSelectedAsync(this, DataContext as DeploymentsViewModel);
}
