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
            settings.BeginBackupOperation($"Iniciando creación. Destino seleccionado: {file.Path.LocalPath}");
            var progress = new Progress<string>(settings.AppendBackupLog);
            var result = await Task.Run(async () => await _backupService.CreateAsync(file.Path.LocalPath, password, progress).ConfigureAwait(false));
            settings.AppendBackupLog($"ÉXITO: {result.FileCount} archivos cifrados ({FormatSize(result.SizeBytes)}). Guarda la contraseña en un lugar seguro.");
        }
        catch (Exception ex)
        {
            settings.AppendBackupLog($"ERROR al crear copia: {ex}");
            settings.SetBackupStatus($"No se pudo crear la copia: {ex.Message}");
        }
        finally
        {
            settings.EndBackupOperation();
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
            settings.BeginBackupOperation($"Iniciando restauración. Archivo: {file.Path.LocalPath}");
            var progress = new Progress<string>(settings.AppendBackupLog);
            var result = await Task.Run(async () => await _backupService.RestoreAsync(file.Path.LocalPath, password, progress).ConfigureAwait(false));
            settings.AppendBackupLog($"ÉXITO: {result.TotalFiles} archivos aplicados (datos: {result.RoamingFiles + result.LocalFiles}, secretos: {result.SecretFiles}, .env: {(result.EnvironmentRestored ? "sí" : "no")}). Reinicia VPS Desk para cargar los datos.");
        }
        catch (Exception ex)
        {
            settings.AppendBackupLog($"ERROR al restaurar: {ex}");
            settings.SetBackupStatus($"No se pudo restaurar la copia: {ex.Message}. Revisa el registro detallado.");
        }
        finally
        {
            settings.EndBackupOperation();
        }
    }

    private static string FormatSize(long bytes)
        => bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024d:F1} KB",
            _ => $"{bytes / 1024d / 1024d:F1} MB"
        };
}
