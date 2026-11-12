using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using VpsDesk.Desktop.Localization;
using VpsDesk.Desktop.Services;
using VpsDesk.Desktop.ViewModels;

namespace VpsDesk.Desktop.Views;

public partial class BackupsView : UserControl
{
    public BackupsView() => InitializeComponent();

    // Tocar el fondo (no la tarjeta) cierra la capa superior, igual que «atrás» en el teléfono.
    private void OnModalScrimPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, sender)) return;
        (DataContext as DeploymentsViewModel)?.TryDismissTopLayer();
    }

    private async void ExportBackup_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DeploymentsViewModel { CanExportSelectedBackup: true } model
            || model.SelectedAppBackup is not { } backup || TopLevel.GetTopLevel(this) is not { } topLevel) return;
        try
        {
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = LocalizationService.T("Backups_Export"),
                SuggestedFileName = backup.FileName,
                FileTypeChoices = [new FilePickerFileType("VPS Desk") { Patterns = ["*.vpsbackup.tar"] }]
            });
            if (file is null || model.SelectedAppBackup != backup || !model.CanExportSelectedBackup) return;
            if (file.TryGetLocalPath() is { } localPath && string.Equals(Path.GetFullPath(localPath), Path.GetFullPath(backup.FullPath),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                Toast.Warning(LocalizationService.T("Backups_ExportDifferentPath"));
                return;
            }
            await using var output = await file.OpenWriteAsync();
            if (output.CanSeek) output.SetLength(0);
            await model.ExportSelectedBackupAsync(output);
        }
        catch (Exception ex)
        {
            Toast.Error(ex, LocalizationService.T("Backups_Export"));
        }
    }
}
