using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
using VpsDesk.Desktop;

namespace VpsDesk.Android;

[Activity(
    Label = "VPS Desk",
    Theme = "@style/MyTheme.NoActionBar",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation
        | ConfigChanges.ScreenSize
        | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        => base.CustomizeAppBuilder(builder)
            .WithInterFont();
}
