using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Application.Runtime;

namespace VpsDesk.Desktop.ViewModels;

/// <summary>
/// Modo ahorro de datos. En "Automático" se activa solo con una red medida (datos móviles);
/// se evalúa en el momento de ejecutar, no al abrir la pantalla, porque el teléfono puede
/// cambiar de wifi a datos entre una cosa y otra.
/// Efectos: la salida de cada paso no se transmite en vivo (solo vuelve su final, acotado) y
/// la copia previa se queda en el VPS en vez de descargarse al dispositivo.
/// </summary>
public partial class DeploymentsViewModel
{
    private static readonly string DataSaverPreferencePath =
        Path.Combine(VpsDesk.Application.Runtime.VpsDeskDataPaths.RoamingRoot, "data-saver.txt");

    /// <summary>0 = Automático, 1 = Siempre, 2 = Nunca (índice del selector).</summary>
    [ObservableProperty] private int _dataSaverModeIndex = LoadDataSaverPreference();

    public DataSaverMode DataSaverMode => (DataSaverMode)Math.Clamp(DataSaverModeIndex, 0, 2);

    public bool IsDataSaverActive => DataSaverPolicy.IsActive(DataSaverMode);

    public string DataSaverStatus
    {
        get
        {
            var metered = DataSaverPolicy.IsMeteredSafe();
            var network = metered ? "red medida (datos móviles)" : "red sin límite de datos";
            return DataSaverMode switch
            {
                DataSaverMode.Always => $"Activo siempre · {network}",
                DataSaverMode.Never => $"Desactivado · {network}",
                _ => metered ? $"Activo automáticamente · {network}" : $"Inactivo · {network}"
            };
        }
    }

    partial void OnDataSaverModeIndexChanged(int value)
    {
        RefreshDataSaverStatus();
        NotifyProtectionsChanged();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DataSaverPreferencePath)!);
            File.WriteAllText(DataSaverPreferencePath, Math.Clamp(value, 0, 2).ToString());
        }
        catch
        {
            // Es una preferencia de comodidad: si no se puede guardar, vale para esta sesión.
        }
    }

    /// <summary>Vuelve a leer el tipo de red; se llama al ejecutar y desde el botón de la tarjeta.</summary>
    [RelayCommand]
    public void RefreshDataSaverStatus()
    {
        OnPropertyChanged(nameof(DataSaverMode));
        OnPropertyChanged(nameof(IsDataSaverActive));
        OnPropertyChanged(nameof(DataSaverStatus));
        NotifyExecutionSummaryChanged();
    }

    private string BuildDataSaverNotice()
        => IsDataSaverActive
            ? " Modo ahorro de datos: el log no se transmite en vivo y la copia previa queda en el VPS."
            : string.Empty;

    private static int LoadDataSaverPreference()
    {
        try
        {
            return File.Exists(DataSaverPreferencePath)
                && int.TryParse(File.ReadAllText(DataSaverPreferencePath).Trim(), out var value)
                    ? Math.Clamp(value, 0, 2)
                    : 0;
        }
        catch
        {
            return 0;
        }
    }
}
