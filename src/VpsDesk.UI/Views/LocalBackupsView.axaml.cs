using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using VpsDesk.Desktop.Localization;
using VpsDesk.Desktop.Services;
using VpsDesk.Desktop.ViewModels;

namespace VpsDesk.Desktop.Views;

public partial class LocalBackupsView : UserControl
{
    private readonly EncryptedBackupService _service = new();
    private TaskCompletionSource<string?>? _prompt;
    private bool _passwordRequired;
    private bool _confirmationRequired;
    private bool _choosingFile;

    public LocalBackupsView()
    {
        InitializeComponent();
        DetachedFromVisualTree += (_, _) => CompletePrompt(null);
    }

    private async void ExportLocal_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel { IsBackupOperationRunning: false } model || _choosingFile
            || TopLevel.GetTopLevel(this) is not { } topLevel) return;
        _choosingFile = true;
        var ownsOperation = false;
        try
        {
            var encrypt = model.EncryptLocalBackup;
            var credentials = model.IncludeBackupCredentials;
            var password = encrypt ? await AskAsync(false, true) : string.Empty;
            if (password is null) return;
            var extension = encrypt ? "vpsbackup" : "zip";
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = LocalizationService.T("Backups_ExportLocal"),
                SuggestedFileName = $"VPS-Desk-datos-{DateTime.Now:yyyyMMdd-HHmmss}.{extension}",
                DefaultExtension = extension,
                FileTypeChoices = [new FilePickerFileType("VPS Desk") { Patterns = ["*." + extension] }]
            });
            if (file is null || model.IsBackupOperationRunning) return;
            model.BeginBackupOperation(LocalizationService.T("Backups_LocalExportRunning"));
            ownsOperation = true;
            var progress = new Progress<string>(model.AppendBackupLog);
            // Finish the archive before opening the selected destination. This also supports Android content URIs.
            var temporary = Path.Combine(Path.GetTempPath(), "vpsdesk-export-" + Guid.NewGuid().ToString("N"));
            try
            {
                await Task.Run(async () =>
                {
                    await using var staging = File.Create(temporary);
                    await _service.CreatePortableAsync(staging, encrypt, password, credentials, progress);
                });
                await using var output = await file.OpenWriteAsync();
                if (output.CanSeek) output.SetLength(0);
                await using var input = File.OpenRead(temporary);
                await input.CopyToAsync(output);
                await output.FlushAsync();
                model.AppendBackupLog(LocalizationService.T("Backups_LocalExportDone"));
            }
            finally { File.Delete(temporary); }
        }
        catch (Exception ex) { ReportFailure(ex); }
        finally
        {
            _choosingFile = false;
            if (ownsOperation) (DataContext as SettingsViewModel)?.EndBackupOperation();
        }
    }

    private async void ImportLocal_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel { IsBackupOperationRunning: false } model || _choosingFile
            || TopLevel.GetTopLevel(this) is not { } topLevel) return;
        _choosingFile = true;
        var ownsOperation = false;
        try
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = LocalizationService.T("Backups_ImportLocal"),
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("VPS Desk") { Patterns = ["*.vpsbackup", "*.zip"], MimeTypes = ["application/zip", "application/octet-stream"] }]
            });
            if (files.FirstOrDefault() is not { } file) return;
            bool encrypted;
            await using (var header = await file.OpenReadAsync()) encrypted = await EncryptedBackupService.IsEncryptedAsync(header);
            var password = await AskAsync(true, encrypted, file.Name);
            if (password is null || model.IsBackupOperationRunning) return;
            model.BeginBackupOperation(LocalizationService.T("Backups_LocalRestoreRunning") + " " + file.Name);
            ownsOperation = true;
            var progress = new Progress<string>(model.AppendBackupLog);
            await using var input = await file.OpenReadAsync();
            var result = await Task.Run(() => _service.RestorePortableAsync(input, password, progress));
            model.AppendBackupLog(LocalizationService.Current.Format("Backups_LocalRestoreDone", result.TotalFiles));
        }
        catch (Exception ex) { ReportFailure(ex); }
        finally
        {
            _choosingFile = false;
            if (ownsOperation) (DataContext as SettingsViewModel)?.EndBackupOperation();
        }
    }

    private Task<string?> AskAsync(bool restoring, bool encrypted, string? fileName = null)
    {
        _passwordRequired = encrypted;
        _confirmationRequired = !restoring && encrypted;
        PromptTitle.Text = LocalizationService.T(restoring ? "Backups_ImportLocal" : "Backups_ExportLocal");
        PromptMessage.Text = LocalizationService.T(restoring ? "Backups_LocalRestoreConfirm" : "Backups_PasswordHelp");
        if (fileName is not null) PromptMessage.Text = fileName + Environment.NewLine + Environment.NewLine + PromptMessage.Text;
        PasswordPanel.IsVisible = encrypted;
        ConfirmationPanel.IsVisible = _confirmationRequired;
        PromptError.Text = string.Empty;
        PasswordInput.Text = string.Empty;
        PasswordConfirmation.Text = string.Empty;
        _prompt = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        PromptPanel.IsVisible = true;
        return _prompt.Task;
    }

    private void AcceptPrompt_Click(object? sender, RoutedEventArgs e)
    {
        var password = PasswordInput.Text ?? string.Empty;
        if (_passwordRequired && password.Length < 12)
        {
            PromptError.Text = LocalizationService.T("Backups_PasswordLength");
            return;
        }
        if (_confirmationRequired && password != PasswordConfirmation.Text)
        {
            PromptError.Text = LocalizationService.T("Backups_PasswordMismatch");
            return;
        }
        CompletePrompt(password);
    }

    private void CancelPrompt_Click(object? sender, RoutedEventArgs e) => CompletePrompt(null);

    private void CompletePrompt(string? password)
    {
        PromptPanel.IsVisible = false;
        PasswordInput.Text = string.Empty;
        PasswordConfirmation.Text = string.Empty;
        _prompt?.TrySetResult(password);
        _prompt = null;
    }

    private void ReportFailure(Exception ex)
    {
        var message = UserFacingErrors.Describe(ex);
        (DataContext as SettingsViewModel)?.AppendBackupLog(message);
        Toast.Error(message, LocalizationService.T("Backups_LocalDataTitle"));
    }
}
