using Avalonia.Controls;

namespace VpsDesk.Desktop.Views;

/// <summary>
/// Abre y cierra una hoja inferior con su fondo. Las animaciones están en
/// MobileStyles.axaml (clases "open"), así que todas las hojas se mueven igual.
/// </summary>
internal static class MobileSheet
{
    public static void SetOpen(Control scrim, Control sheet, bool isOpen)
    {
        if (isOpen)
        {
            scrim.Classes.Add("open");
            sheet.Classes.Add("open");
        }
        else
        {
            scrim.Classes.Remove("open");
            sheet.Classes.Remove("open");
        }
    }
}
