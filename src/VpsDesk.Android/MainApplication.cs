using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using VpsDesk.Application.Runtime;
using VpsDesk.Application.Updates;
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

            // Las copias de la aplicación se guardan junto al .env: en Android, la carpeta
            // "Documentos" de .NET es privada y no se ve desde el gestor de archivos. Esta
            // carpeta sí se puede sacar con `adb pull` o por USB desde el PC.
            Environment.SetEnvironmentVariable(
                "VPSDESK_BACKUP_DIR",
                System.IO.Path.Combine(externalFilesDir, "Backups"));
        }

        // El modo ahorro de datos (Automático) decide según esta red; sin esto nunca se activaría.
        NetworkConditions.Current = new AndroidNetworkConditions(this);

        // Actualización in-app: lee versión instalada y la URL de version.json compilada en el APK.
        AppUpdatePlatform.Current = new AndroidAppUpdatePlatform(this);

        base.OnCreate();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        => base.CustomizeAppBuilder(builder)
            .WithInterFont();
}
