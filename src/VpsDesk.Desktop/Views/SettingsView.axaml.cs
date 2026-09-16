using Avalonia.Controls;
using Avalonia.Platform.Storage;
using VpsDesk.Desktop.Services;
using VpsDesk.Desktop.ViewModels;

namespace VpsDesk.Desktop.Views;

public partial class SettingsView : UserControl
{
    private readonly EncryptedBackupService _backupService = new();

    public SettingsView()
    {
        InitializeComponent();
    }

    private async void CreateEncryptedBackup_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is not Window owner || DataContext is not SettingsViewModel settings) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar copia cifrada de VPS Desk",
            SuggestedFileName = $"VPS-Desk-{DateTime.Now:yyyyMMdd}.vpsbackup",
            DefaultExtension = "vpsbackup",
            FileTypeChoices = [new FilePickerFileType("Copia cifrada de VPS Desk") { Patterns = ["*.vpsbackup"] }]
        });
        if (file is null) return;

        var password = await new BackupPasswordDialog(
            "Crear copia cifrada",
            "La contraseña no se guarda. Sin ella no será posible restaurar esta copia.",
            requiresConfirmation: true).ShowDialog<string?>(owner);
        if (string.IsNullOrEmpty(password)) return;

        try
        {
            settings.SetBackupStatus("Creando copia cifrada...");
            await _backupService.CreateAsync(file.Path.LocalPath, password);
            settings.SetBackupStatus("Copia cifrada creada correctamente. Guarda la contraseña en un lugar seguro.");
        }
        catch (Exception ex)
        {
            settings.SetBackupStatus($"No se pudo crear la copia: {ex.Message}");
        }
    }

    private async void RestoreEncryptedBackup_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is not Window owner || DataContext is not SettingsViewModel settings) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Seleccionar copia cifrada de VPS Desk",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Copia cifrada de VPS Desk") { Patterns = ["*.vpsbackup"] }]
        });
        var file = files.FirstOrDefault();
        if (file is null) return;

        var password = await new BackupPasswordDialog(
            "Restaurar copia cifrada",
            "La configuración se reemplazará al restaurar. Cierra y vuelve a abrir VPS Desk al finalizar.",
            requiresConfirmation: false).ShowDialog<string?>(owner);
        if (string.IsNullOrEmpty(password)) return;

        try
        {
            settings.SetBackupStatus("Restaurando copia cifrada...");
            await _backupService.RestoreAsync(file.Path.LocalPath, password);
            settings.SetBackupStatus("Copia restaurada. Cierra y vuelve a abrir VPS Desk para cargar los datos restaurados.");
        }
        catch (Exception ex)
        {
            settings.SetBackupStatus($"No se pudo restaurar la copia: {ex.Message}");
        }
    }
}
