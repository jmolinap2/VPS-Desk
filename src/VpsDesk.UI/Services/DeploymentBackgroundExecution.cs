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
}
