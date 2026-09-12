using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using VpsDesk.Desktop.Services;
using VpsDesk.Desktop.ViewModels;
using VpsDesk.Desktop.Views;
using VpsDesk.Infrastructure.Monitoring;
using VpsDesk.Infrastructure.Ssh;

namespace VpsDesk.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var bootstrap = BootstrapServerProfileLoader.Load();
            var ssh = new SshNetCommandExecutor();
            var probe = new LinuxServerProbeService(ssh);

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(
                    probe,
                    bootstrap.Profile,
                    bootstrap.Secret,
                    bootstrap.Warning)
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
