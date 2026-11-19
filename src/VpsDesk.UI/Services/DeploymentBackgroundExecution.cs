namespace VpsDesk.Desktop.Services;

/// <summary>Platform hook for keeping an active deployment visible and running.</summary>
public static class DeploymentBackgroundExecution
{
    public static Func<IDisposable>? Start { get; set; }
    public static Action<string>? Report { get; set; }
    public static Action<string, string>? Complete { get; set; }
}
