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
}
