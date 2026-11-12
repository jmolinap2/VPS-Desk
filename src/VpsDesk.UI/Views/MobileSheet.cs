using Avalonia.Controls;

namespace VpsDesk.Desktop.Views;

/// <summary>
/// Abre y cierra una hoja inferior con su fondo. Las animaciones están en
/// MobileStyles.axaml (clases "open"), así que todas las hojas se mueven igual.
/// Las hojas abiertas se registran para que el botón «atrás» las cierre (la más reciente primero).
/// </summary>
internal static class MobileSheet
{
    private static readonly List<(Control Scrim, Control Sheet)> OpenSheets = new();

    public static void SetOpen(Control scrim, Control sheet, bool isOpen)
    {
        OpenSheets.RemoveAll(x => ReferenceEquals(x.Sheet, sheet));
        if (isOpen)
        {
            scrim.Classes.Add("open");
            sheet.Classes.Add("open");
            OpenSheets.Add((scrim, sheet));
        }
        else
        {
            scrim.Classes.Remove("open");
            sheet.Classes.Remove("open");
        }
    }

    /// <summary>Cierra la hoja visible abierta más recientemente; false si no hay ninguna.</summary>
    public static bool TryDismissTop()
    {
        for (var i = OpenSheets.Count - 1; i >= 0; i--)
        {
            var (scrim, sheet) = OpenSheets[i];
            OpenSheets.RemoveAt(i);
            if (!sheet.IsEffectivelyVisible) continue;

            scrim.Classes.Remove("open");
            sheet.Classes.Remove("open");
            return true;
        }

        return false;
    }
}
