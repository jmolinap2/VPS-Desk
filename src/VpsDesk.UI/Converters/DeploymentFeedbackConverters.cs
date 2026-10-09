using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using VpsDesk.Application.Deployments;

namespace VpsDesk.Desktop.Converters;

public sealed class DeploymentStatusMessageConverter : IValueConverter
{
    private static readonly IBrush InfoBackground = Brush.Parse("#101B2A");
    private static readonly IBrush InfoBorder = Brush.Parse("#294A70");
    private static readonly IBrush InfoForeground = Brush.Parse("#9CC8F5");
    private static readonly IBrush SuccessBackground = Brush.Parse("#0D241F");
    private static readonly IBrush SuccessBorder = Brush.Parse("#0F766E");
    private static readonly IBrush SuccessForeground = Brush.Parse("#7EE7CD");
    private static readonly IBrush WarningBackground = Brush.Parse("#2A2410");
    private static readonly IBrush WarningBorder = Brush.Parse("#A16207");
    private static readonly IBrush WarningForeground = Brush.Parse("#FCD34D");
    private static readonly IBrush ErrorBackground = Brush.Parse("#2A1215");
    private static readonly IBrush ErrorBorder = Brush.Parse("#B91C1C");
    private static readonly IBrush ErrorForeground = Brush.Parse("#FCA5A5");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var message = value as string ?? string.Empty;
        var tone = Classify(message);
        var part = parameter as string ?? "foreground";

        return (tone, part) switch
        {
            (Tone.Success, "background") => SuccessBackground,
            (Tone.Success, "border") => SuccessBorder,
            (Tone.Success, "foreground") => SuccessForeground,
            (Tone.Success, "glyph") => "✓",
            (Tone.Warning, "background") => WarningBackground,
            (Tone.Warning, "border") => WarningBorder,
            (Tone.Warning, "foreground") => WarningForeground,
            (Tone.Warning, "glyph") => "!",
            (Tone.Error, "background") => ErrorBackground,
            (Tone.Error, "border") => ErrorBorder,
            (Tone.Error, "foreground") => ErrorForeground,
            (Tone.Error, "glyph") => "×",
            (_, "background") => InfoBackground,
            (_, "border") => InfoBorder,
            (_, "glyph") => "i",
            _ => InfoForeground
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static Tone Classify(string message)
    {
        if (ContainsAny(message,
                "fall", "bloque", "inválid", "invalid", "obligator", "no hay", "no se pudo",
                "detuvo", "problemas", "cancelad", "rechaz", "error"))
        {
            return Tone.Error;
        }

        if (ContainsAny(message, "advert", "warning", "warnings", "con advertencias", "configuración cambió"))
        {
            return Tone.Warning;
        }

        if (ContainsAny(message,
                "listo", "correcto", "pasó", "passed", "completad", "guardado", "healthy", "verificad",
                "detectado con receta", "modo genérico listo"))
        {
            return Tone.Success;
        }

        return Tone.Info;
    }

    private static bool ContainsAny(string value, params string[] terms)
        => terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private enum Tone
    {
        Info,
        Success,
        Warning,
        Error
    }
}

public sealed class PreflightStatusConverter : IValueConverter
{
    private static readonly IBrush PassedBackground = Brush.Parse("#0D241F");
    private static readonly IBrush PassedBorder = Brush.Parse("#0F766E");
    private static readonly IBrush PassedForeground = Brush.Parse("#7EE7CD");
    private static readonly IBrush WarningBackground = Brush.Parse("#2A2410");
    private static readonly IBrush WarningBorder = Brush.Parse("#A16207");
    private static readonly IBrush WarningForeground = Brush.Parse("#FCD34D");
    private static readonly IBrush FailedBackground = Brush.Parse("#2A1215");
    private static readonly IBrush FailedBorder = Brush.Parse("#B91C1C");
    private static readonly IBrush FailedForeground = Brush.Parse("#FCA5A5");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = value is PreflightCheckStatus typed ? typed : PreflightCheckStatus.Warning;
        var part = parameter as string ?? "foreground";

        return (status, part) switch
        {
            (PreflightCheckStatus.Passed, "background") => PassedBackground,
            (PreflightCheckStatus.Passed, "border") => PassedBorder,
            (PreflightCheckStatus.Passed, "glyph") => "✓",
            (PreflightCheckStatus.Passed, "label") => "OK",
            (PreflightCheckStatus.Passed, _) => PassedForeground,
            (PreflightCheckStatus.Failed, "background") => FailedBackground,
            (PreflightCheckStatus.Failed, "border") => FailedBorder,
            (PreflightCheckStatus.Failed, "glyph") => "×",
            (PreflightCheckStatus.Failed, "label") => "FALLO",
            (PreflightCheckStatus.Failed, _) => FailedForeground,
            (_, "background") => WarningBackground,
            (_, "border") => WarningBorder,
            (_, "glyph") => "!",
            (_, "label") => "AVISO",
            _ => WarningForeground
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class PostflightStatusConverter : IValueConverter
{
    private static readonly IBrush PassedBackground = Brush.Parse("#0D241F");
    private static readonly IBrush PassedBorder = Brush.Parse("#0F766E");
    private static readonly IBrush PassedForeground = Brush.Parse("#7EE7CD");
    private static readonly IBrush WarningBackground = Brush.Parse("#2A2410");
    private static readonly IBrush WarningBorder = Brush.Parse("#A16207");
    private static readonly IBrush WarningForeground = Brush.Parse("#FCD34D");
    private static readonly IBrush FailedBackground = Brush.Parse("#2A1215");
    private static readonly IBrush FailedBorder = Brush.Parse("#B91C1C");
    private static readonly IBrush FailedForeground = Brush.Parse("#FCA5A5");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = value is PostDeployCheckStatus typed ? typed : PostDeployCheckStatus.Warning;
        var part = parameter as string ?? "foreground";

        return (status, part) switch
        {
            (PostDeployCheckStatus.Passed, "background") => PassedBackground,
            (PostDeployCheckStatus.Passed, "border") => PassedBorder,
            (PostDeployCheckStatus.Passed, "glyph") => "✓",
            (PostDeployCheckStatus.Passed, "label") => "OK",
            (PostDeployCheckStatus.Passed, _) => PassedForeground,
            (PostDeployCheckStatus.Failed, "background") => FailedBackground,
            (PostDeployCheckStatus.Failed, "border") => FailedBorder,
            (PostDeployCheckStatus.Failed, "glyph") => "×",
            (PostDeployCheckStatus.Failed, "label") => "FALLO",
            (PostDeployCheckStatus.Failed, _) => FailedForeground,
            (_, "background") => WarningBackground,
            (_, "border") => WarningBorder,
            (_, "glyph") => "!",
            (_, "label") => "AVISO",
            _ => WarningForeground
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
