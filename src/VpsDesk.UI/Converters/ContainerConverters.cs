using System.Globalization;
using Avalonia.Data.Converters;
using VpsDesk.Desktop.Localization;

namespace VpsDesk.Desktop.Converters;

/// <summary>Estado de un contenedor Docker en el idioma de la interfaz (running → En ejecución).</summary>
public sealed class ContainerStateLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string state
            ? state.ToLowerInvariant() switch
            {
                "running" => LocalizationService.T("Containers_Running"),
                "exited" or "dead" or "created" => LocalizationService.T("Containers_StoppedOne"),
                _ => state
            }
            : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
