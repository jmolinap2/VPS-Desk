using System.Text;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using VpsDesk.Desktop.Localization;
using VpsDesk.Desktop.Services;
using VpsDesk.Desktop.ViewModels;

namespace VpsDesk.Desktop.Views;

/// <summary>
/// Copiar o guardar el informe de un pase. Los botones llevan <c>Tag="run"</c> (ejecución actual)
/// o <c>Tag="history"</c> (entrada abierta del historial); los usan la vista de escritorio y la móvil.
/// </summary>
internal static class DeploymentReportActions
{
    public static async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control owner) return;
        var text = BuildText(owner, out _);
        if (text is not null) await CopyTextAsync(owner, text);
    }

    public static async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control owner) return;
        var text = BuildText(owner, out var fileName);
        if (text is not null) await SaveTextAsync(owner, text, fileName);
    }

    /// <summary>Copia un informe al portapapeles y avisa con un aviso breve; la usan también otros informes de texto.</summary>
    public static async Task CopyTextAsync(Control owner, string text)
    {
        if (TopLevel.GetTopLevel(owner)?.Clipboard is not { } clipboard) return;
        try
        {
            await clipboard.SetTextAsync(text);
            Toast.Success(LocalizationService.T("Report_Copied"));
        }
        catch (Exception ex)
        {
            Toast.Error(ex, LocalizationService.T("Report_Copy"));
        }
    }

    /// <summary>Guarda un informe como archivo de texto con el selector del sistema (también en Android).</summary>
    public static async Task SaveTextAsync(Control owner, string text, string fileName)
    {
        if (TopLevel.GetTopLevel(owner) is not { } topLevel) return;
        try
        {
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = LocalizationService.T("Report_Save"),
                SuggestedFileName = fileName,
                DefaultExtension = "txt",
                FileTypeChoices = [FilePickerFileTypes.TextPlain]
            });
            if (file is null) return;
            await using var output = await file.OpenWriteAsync();
            if (output.CanSeek) output.SetLength(0);
            await output.WriteAsync(new UTF8Encoding(false).GetBytes(text));
            Toast.Success(LocalizationService.T("Report_Saved"));
        }
        catch (Exception ex)
        {
            Toast.Error(ex, LocalizationService.T("Report_Save"));
        }
    }

    private static string? BuildText(Control owner, out string fileName)
    {
        fileName = "pase.txt";
        if (owner.DataContext is not DeploymentsViewModel model) return null;
        var data = owner is Control { Tag: "history" } ? model.BuildHistoryReportData() : model.BuildRunReportData();
        if (data is null) return null;
        fileName = DeploymentReport.SuggestFileName(data);
        return DeploymentReport.Build(data);
    }
}
