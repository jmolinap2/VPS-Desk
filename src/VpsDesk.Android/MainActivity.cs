using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace VpsDesk.Android;

[Activity(
    Label = "VPS Desk",
    Theme = "@style/MyTheme.NoActionBar",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity
{
}
