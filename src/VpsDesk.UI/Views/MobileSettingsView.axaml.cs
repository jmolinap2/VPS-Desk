using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using VpsDesk.Desktop.Services;
using VpsDesk.Desktop.ViewModels;

namespace VpsDesk.Desktop.Views;

/// <summary>
/// Ajustes para teléfono. La lógica de copia cifrada es la misma que la de
/// escritorio, pero la contraseña se pide en una hoja inferior: en Android la
/// app es una sola vista y no puede abrir un diálogo de ventana.
/// </summary>
public partial class MobileSettingsView : UserControl
{
    private readonly EncryptedBackupService _backupService = new();
    private TaskCompletionSource<string?>? _passwordRequest;
    private bool _passwordNeedsConfirmation;

    public MobileSettingsView()
    {
        InitializeComponent();
    }

    private async void CreateEncryptedBackup_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || DataContext is not SettingsViewModel settings) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar copia cifrada de VPS Desk",
            SuggestedFileName = $"VPS-Desk-{DateTime.Now:yyyyMMdd}.vpsbackup",
            DefaultExtension = "vpsbackup",
            FileTypeChoices = [new FilePickerFileType("Copia cifrada de VPS Desk") { Patterns = ["*.vpsbackup"] }]
        });
        if (file is null) return;

        var password = await AskPasswordAsync(
            "Crear copia cifrada",
            "La contraseña no se guarda. Sin ella no será posible restaurar esta copia.",
            requiresConfirmation: true);
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

    private async void RestoreEncryptedBackup_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || DataContext is not SettingsViewModel settings) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Seleccionar copia cifrada de VPS Desk",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Copia cifrada de VPS Desk") { Patterns = ["*.vpsbackup"] }]
        });
        var file = files.FirstOrDefault();
        if (file is null) return;

        var password = await AskPasswordAsync(
            "Restaurar copia cifrada",
            "La configuración se reemplazará al restaurar. Cierra y vuelve a abrir VPS Desk al finalizar.",
            requiresConfirmation: false);
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

    // Muestra la hoja de contraseña y espera a que el usuario confirme o cancele.
    private Task<string?> AskPasswordAsync(string title, string message, bool requiresConfirmation)
    {
        PasswordTitle.Text = title;
        PasswordMessage.Text = message;
        PasswordInput.Text = string.Empty;
        PasswordConfirmInput.Text = string.Empty;
        PasswordConfirmPanel.IsVisible = requiresConfirmation;
        PasswordError.IsVisible = false;
        _passwordNeedsConfirmation = requiresConfirmation;

        _passwordRequest?.TrySetResult(null);
        _passwordRequest = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        MobileSheet.SetOpen(PasswordScrim, PasswordSheet, true);
        return _passwordRequest.Task;
    }

    private void OnPasswordConfirmClick(object? sender, RoutedEventArgs e)
    {
        // Mismas reglas que el diálogo de escritorio.
        var password = PasswordInput.Text ?? string.Empty;
        if (password.Length < 12)
        {
            ShowPasswordError("Usa una contraseña de al menos 12 caracteres.");
            return;
        }

        if (_passwordNeedsConfirmation && !string.Equals(password, PasswordConfirmInput.Text, StringComparison.Ordinal))
        {
            ShowPasswordError("Las contraseñas no coinciden.");
            return;
        }

        ClosePasswordSheet(password);
    }

    private void OnPasswordCancelClick(object? sender, RoutedEventArgs e) => ClosePasswordSheet(null);

    private void ShowPasswordError(string message)
    {
        PasswordErrorText.Text = message;
        PasswordError.IsVisible = true;
    }

    private void ClosePasswordSheet(string? password)
    {
        MobileSheet.SetOpen(PasswordScrim, PasswordSheet, false);
        _passwordRequest?.TrySetResult(password);
        _passwordRequest = null;
    }

    private static string FormatSize(long bytes)
        => bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024d:F1} KB",
            _ => $"{bytes / 1024d / 1024d:F1} MB"
        };
}
