using System.Globalization;
using Avalonia.Data.Converters;
using VpsDesk.Desktop.Localization;

namespace VpsDesk.Desktop.Converters;

/// <summary>
/// Nombre legible (y localizado) del tipo de fuente de registros. El valor interno sigue siendo
/// el identificador en inglés que usa el ViewModel para decidir qué comando ejecutar.
/// </summary>
public sealed class LogSourceLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            "Docker container" => LocalizationService.T("Logs_SourceDocker"),
            "systemd service" => LocalizationService.T("Logs_SourceSystemd"),
            string other => other,
            _ => string.Empty
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
