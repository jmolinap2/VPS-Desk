using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using VpsDesk.Desktop;

namespace VpsDesk.Android;

[Application]
public class MainApplication : AvaloniaAndroidApplication<App>
{
    public MainApplication(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer)
    {
    }

    public override void OnCreate()
    {
        // Le dice a BootstrapServerProfileLoader exactamente dónde buscar el
        // .env, en vez de que camine AppContext.BaseDirectory hacia arriba
        // (que en Android no tiene nada que encontrar). Usamos el
        // almacenamiento externo específico de la app: no requiere permisos
        // en runtime y se puede llenar con `adb push` sin root.
        var externalFilesDir = GetExternalFilesDir(null)?.AbsolutePath;
        if (!string.IsNullOrWhiteSpace(externalFilesDir))
        {
            Environment.SetEnvironmentVariable(
                "VPSDESK_ENV_PATH",
                System.IO.Path.Combine(externalFilesDir, ".env"));
        }

        base.OnCreate();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        => base.CustomizeAppBuilder(builder)
            .WithInterFont();
}
