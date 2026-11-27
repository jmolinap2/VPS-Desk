namespace VpsDesk.Desktop.Services;

/// <summary>Platform hook for keeping an active deployment visible and running.</summary>
public static class DeploymentBackgroundExecution
{
    public static Func<IDisposable>? Start { get; set; }
    public static Action<string>? Report { get; set; }
    public static Action<string, string>? Complete { get; set; }

    /// <summary>
    /// Pide abortar el pase en curso desde fuera de la pantalla (p. ej. la burbuja flotante de Android).
    /// Solo tiene valor mientras hay un pase corriendo; puede llamarse desde cualquier hilo.
    /// </summary>
    public static Action? RequestAbort { get; set; }

    /// <summary>
    /// Minimiza la aplicación y deja el pase en curso como burbuja flotante (Android). Es null donde la plataforma
    /// no lo ofrece, y entonces la interfaz no muestra el botón «Minimizar».
    /// </summary>
    public static Action? Minimize { get; set; }

    /// <summary>Avance del pase: porcentaje (0-100) y paso actual. La burbuja flotante lo muestra como progreso real.</summary>
    public static Action<double, string>? Progress { get; set; }

    /// <summary>Las últimas líneas de la salida del pase, para la vista en vivo de la burbuja flotante.</summary>
    public static Action<string>? OutputTail { get; set; }
}
