using System.Globalization;
using Avalonia.Data.Converters;
using VpsDesk.Application.AppBackups;
using VpsDesk.Desktop.Localization;

namespace VpsDesk.Desktop.Converters;

/// <summary>Tamaño legible (KB, MB, GB). Con el parámetro «kb» el valor de entrada son kilobytes.</summary>
public sealed class BackupSizeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var number = value switch
        {
            long l => (double)l,
            int i => i,
            double d => d,
            decimal m => (double)m,
            _ => 0d
        };
        if (parameter as string == "kb") number *= 1024;

        return number switch
        {
            <= 0 => "—",
            < 1024 * 1024 => string.Create(culture, $"{Math.Max(1, number / 1024):0} KB"),
            < 1024d * 1024 * 1024 => string.Create(culture, $"{number / 1024 / 1024:0.#} MB"),
            _ => string.Create(culture, $"{number / 1024 / 1024 / 1024:0.##} GB")
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Fecha local y corta, sin el desfase horario (+00:00) que no le sirve a nadie.</summary>
public sealed class BackupDateConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DateTimeOffset date ? date.ToLocalTime().ToString("dd/MM/yyyy HH:mm", culture) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Etiqueta localizada del tipo de copia (completa, solo base de datos, solo archivos).</summary>
public sealed class BackupContentLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => LocalizationService.T(value switch
        {
            AppBackupContent.DatabaseOnly => "Backups_KindDatabase",
            AppBackupContent.FilesOnly => "Backups_KindFiles",
            _ => "Backups_KindFull"
        });

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
