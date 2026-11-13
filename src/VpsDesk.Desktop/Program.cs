using Avalonia;

namespace VpsDesk.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Services.NotificationAudio.PlaySound = level =>
        {
            if (OperatingSystem.IsWindows())
                MessageBeep(level switch
                {
                    Services.NotificationLevel.Error => 0x10u,
                    Services.NotificationLevel.Warning => 0x30u,
                    Services.NotificationLevel.Success => 0x40u,
                    _ => 0u
                });
        };
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool MessageBeep(uint type);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
