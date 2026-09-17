using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace VpsDesk.Android;

public sealed class VpsDeskAndroidApp : Avalonia.Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IActivityApplicationLifetime activityLifetime)
        {
            activityLifetime.MainViewFactory = static () => new AndroidConnectivityView();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
