using System.Globalization;
using Avalonia.Data.Converters;
using VpsDesk.Desktop.Localization;
using VpsDesk.Domain.Security;

namespace VpsDesk.Desktop.Converters;

/// <summary>Etiqueta localizada de la severidad de una comprobación (en vez del nombre del enum en inglés).</summary>
public sealed class SecuritySeverityLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => LocalizationService.T(value switch
        {
            SecurityCheckSeverity.Passed => "Security_SeverityPassed",
            SecurityCheckSeverity.Warning => "Security_SeverityWarning",
            SecurityCheckSeverity.Critical => "Security_SeverityCritical",
            _ => "Security_SeverityInfo"
        });

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
