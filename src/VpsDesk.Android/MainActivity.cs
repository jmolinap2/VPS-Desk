using Android.App;
using Android.Content.PM;
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

    internal static void RequestDeploymentNotifications()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33)
            && _current?.TryGetTarget(out var activity) == true
            && activity.CheckSelfPermission(global::Android.Manifest.Permission.PostNotifications) != Permission.Granted)
            activity.RequestPermissions([global::Android.Manifest.Permission.PostNotifications], 1401);
    }
}
