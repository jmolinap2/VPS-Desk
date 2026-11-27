using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Provider;
using Android.Views;
using Avalonia.Android;

namespace VpsDesk.Android;

[Activity(
    Label = "VPS Desk",
    Theme = "@style/MyTheme.NoActionBar",
    MainLauncher = true,
    // La app ajusta su propio diseño al teclado (ver MobileShell). Sin esto, Android
    // desplaza toda la ventana por su cuenta y deja la interfaz mal colocada.
    WindowSoftInputMode = SoftInput.AdjustNothing,
    ConfigurationChanges = ConfigChanges.Orientation
        | ConfigChanges.ScreenSize
        | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    private static WeakReference<MainActivity>? _current;

    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _current = new WeakReference<MainActivity>(this);
    }

    // La burbuja flotante del pase solo se muestra con la app fuera de primer plano.
    protected override void OnStart()
    {
        base.OnStart();
        DeploymentOverlay.Hide();
    }

    protected override void OnStop()
    {
        DeploymentOverlay.ShowIfRunning();
        base.OnStop();
    }

    /// <summary>
    /// Botón «Minimizar» del pase: manda la app a segundo plano y deja el pase como burbuja flotante.
    /// La primera vez lleva al usuario a conceder «Mostrar sobre otras apps»; al volver, basta pulsar de nuevo.
    /// </summary>
    internal static void MinimizeToBubble()
    {
        MainActivity? activity = null;
        if (_current is null || !_current.TryGetTarget(out activity) || activity is null) return;
        activity.RunOnUiThread(() =>
        {
            if (!DeploymentOverlay.CanDraw(activity))
            {
                global::Android.Widget.Toast.MakeText(activity,
                    "Permite a VPS Desk mostrarse sobre otras apps y vuelve a pulsar Minimizar.",
                    global::Android.Widget.ToastLength.Long)?.Show();
                activity.StartActivity(new Intent(Settings.ActionManageOverlayPermission,
                    global::Android.Net.Uri.Parse($"package:{activity.PackageName}")));
                return;
            }
            DeploymentOverlay.Show();
            activity.MoveTaskToBack(true);
        });
    }

    internal static void RequestDeploymentNotifications()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33)
            && _current?.TryGetTarget(out var activity) == true
            && activity.CheckSelfPermission(global::Android.Manifest.Permission.PostNotifications) != Permission.Granted)
            activity.RequestPermissions([global::Android.Manifest.Permission.PostNotifications], 1401);
    }
}
