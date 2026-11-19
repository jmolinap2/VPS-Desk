using Avalonia.Controls;
using Avalonia.Input.Platform;
using VpsDesk.Desktop.Localization;
using VpsDesk.Desktop.Services;
using VpsDesk.Desktop.ViewModels;

namespace VpsDesk.Desktop.Views;

internal static class EnvironmentClipboardActions
{
    public static async void CopyVariable(object? sender)
    {
        if (sender is not Control { DataContext: EnvironmentVariableRow row } control || row.IsValueMasked) return;
        await CopyAsync(control, $"{row.Key}={row.Value}");
    }

    public static async void CopyFile(object? sender)
    {
        if (sender is not Control { DataContext: FilesViewModel model } control || !model.IsEnvironmentFile) return;
        await CopyAsync(control, model.EditorContent);
    }

    private static async Task CopyAsync(Control owner, string text)
    {
        if (TopLevel.GetTopLevel(owner)?.Clipboard is not { } clipboard) return;
        try
        {
            await clipboard.SetTextAsync(text);
            Toast.Success(LocalizationService.T("Files_EnvCopied"));
        }
        catch
        {
            Toast.Warning(LocalizationService.T("Files_EnvCopyFailed"));
        }
    }
}
