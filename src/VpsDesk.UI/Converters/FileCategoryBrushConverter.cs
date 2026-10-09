using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using VpsDesk.Domain.Files;

namespace VpsDesk.Desktop.Converters;

/// <summary>
/// Maps a file's category to a themed accent brush for its icon chip.
/// ConverterParameter "tint" returns the soft chip background; "solid" (default) returns the icon color.
/// </summary>
public sealed class FileCategoryBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not RemoteFileCategory category) return Brushes.Transparent;

        var hex = category switch
        {
            RemoteFileCategory.Directory => "#E8A33D",
            RemoteFileCategory.Script => "#59C98D",
            RemoteFileCategory.Config => "#A380FF",
            RemoteFileCategory.Image => "#EF7FB0",
            RemoteFileCategory.Archive => "#9199AD",
            RemoteFileCategory.Binary => "#9199AD",
            _ => "#5B9DFF"
        };

        var color = Color.Parse(hex);
        var tint = string.Equals(parameter?.ToString(), "tint", StringComparison.OrdinalIgnoreCase);
        return new SolidColorBrush(color, tint ? 0.16 : 1.0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
