using System.Globalization;
using Avalonia.Data.Converters;
using VpsDesk.Desktop.Localization;
using VpsDesk.Domain.Activity;

namespace VpsDesk.Desktop.Converters;

/// <summary>Etiqueta localizada del resultado de una operación (correcto / con avisos / fallido).</summary>
public sealed class OutcomeLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => LocalizationService.T(value switch
        {
            OperationOutcome.Success => "Deployments_OutcomeSuccess",
            OperationOutcome.Warning => "Deployments_OutcomeWarning",
            OperationOutcome.Failed => "Deployments_OutcomeFailed",
            _ => "Deployments_OutcomeUnknown"
        });

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
