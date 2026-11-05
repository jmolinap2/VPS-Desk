namespace VpsDesk.Application.Runtime;

/// <summary>Estado de la red del dispositivo que ejecuta VPS Desk.</summary>
public interface INetworkConditions
{
    /// <summary>
    /// True si la red activa cobra o limita datos (datos móviles, wifi marcado como "de uso
    /// medido"). Cada plataforma lo implementa; si no puede saberlo debe devolver false.
    /// </summary>
    bool IsMetered { get; }
}

/// <summary>
/// Punto de acceso a las condiciones de red. Escritorio no lo cambia (red no medida);
/// Android lo sustituye al arrancar por una implementación basada en ConnectivityManager.
/// </summary>
public static class NetworkConditions
{
    private sealed class Unmetered : INetworkConditions
    {
        public bool IsMetered => false;
    }

    public static INetworkConditions Current { get; set; } = new Unmetered();
}

public enum DataSaverMode
{
    /// <summary>Se activa solo cuando la red es medida.</summary>
    Auto,
    Always,
    Never
}

public static class DataSaverPolicy
{
    /// <summary>Se evalúa en el momento de ejecutar: la red puede haber cambiado desde que se abrió la pantalla.</summary>
    public static bool IsActive(DataSaverMode mode)
        => mode switch
        {
            DataSaverMode.Always => true,
            DataSaverMode.Never => false,
            _ => IsMeteredSafe()
        };

    public static bool IsMeteredSafe()
    {
        try
        {
            return NetworkConditions.Current.IsMetered;
        }
        catch
        {
            return false;
        }
    }
}
