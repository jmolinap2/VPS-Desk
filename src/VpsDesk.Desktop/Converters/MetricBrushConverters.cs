using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace VpsDesk.Desktop.Converters;

/// <summary>
/// Semantic metric colors used by the dashboard.
/// CPU: warning from 60%, critical from 80%.
/// Memory and disk: warning from 70%, critical from 85%.
/// </summary>
public sealed class UsageToBrushConverter : IValueConverter
{
    private static readonly IBrush Normal = Brush.Parse("#22C55E");
    private static readonly IBrush Warning = Brush.Parse("#F59E0B");
    private static readonly IBrush Critical = Brush.Parse("#EF4444");
    private static readonly IBrush Neutral = Brush.Parse("#64748B");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (!TryGetUsage(value, out var usage)) return Neutral;

        var metric = parameter?.ToString()?.Trim().ToLowerInvariant();
        var (warningAt, criticalAt) = metric switch
        {
            "memory" => (70d, 85d),
            "disk" => (70d, 85d),
            _ => (60d, 80d)
        };

        if (usage >= criticalAt) return Critical;
        if (usage >= warningAt) return Warning;
        return Normal;
    }

    private static bool TryGetUsage(object? value, out double usage)
    {
        switch (value)
        {
            case double number:
                usage = Math.Clamp(number, 0, 100);
                return true;
            case float number:
                usage = Math.Clamp(number, 0, 100);
                return true;
            case decimal number:
                usage = Math.Clamp((double)number, 0, 100);
                return true;
            case int number:
                usage = Math.Clamp(number, 0, 100);
                return true;
            default:
                usage = 0;
                return false;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class ConnectionToBrushConverter : IValueConverter
{
    private static readonly IBrush Online = Brush.Parse("#22C55E");
    private static readonly IBrush Offline = Brush.Parse("#EF4444");
    private static readonly IBrush Pending = Brush.Parse("#64748B");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = value?.ToString() ?? string.Empty;
        if (status.Contains("online", StringComparison.OrdinalIgnoreCase)) return Online;
        if (status.Contains("offline", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("failed", StringComparison.OrdinalIgnoreCase)) return Offline;
        return Pending;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class CapabilityToBrushConverter : IValueConverter
{
    private static readonly IBrush Available = Brush.Parse("#22C55E");
    private static readonly IBrush Unavailable = Brush.Parse("#64748B");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = value?.ToString() ?? string.Empty;
        return string.IsNullOrWhiteSpace(status)
               || status.Contains("unknown", StringComparison.OrdinalIgnoreCase)
               || status.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
            ? Unavailable
            : Available;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
