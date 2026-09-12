using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using VpsDesk.Desktop.Services;
using VpsDesk.Desktop.ViewModels;
using VpsDesk.Desktop.Views;
using VpsDesk.Infrastructure.Docker;
using VpsDesk.Infrastructure.Logs;
using VpsDesk.Infrastructure.Monitoring;
using VpsDesk.Infrastructure.Ssh;

namespace VpsDesk.Desktop;

public partial class App : Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var bootstrap = BootstrapServerProfileLoader.Load();
            var store = new ServerProfileStore();
            var ssh = new SshNetCommandExecutor();
            var probe = new LinuxServerProbeService(ssh);
            var containers = new DockerContainerService(ssh);
            var logs = new LinuxRemoteLogService(ssh);

            var viewModel = new MainWindowViewModel(probe, store, bootstrap);
            viewModel.InitializeContainers(containers);
            viewModel.InitializeLogs(containers, logs);

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
