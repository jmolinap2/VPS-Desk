using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using VpsDesk.Application.Deployments;
using VpsDesk.Desktop.Services;
using VpsDesk.Desktop.ViewModels;
using VpsDesk.Desktop.Views;
using VpsDesk.Infrastructure.Docker;
using VpsDesk.Infrastructure.Logs;
using VpsDesk.Infrastructure.Monitoring;
using VpsDesk.Infrastructure.Ssh;
using VpsDesk.Infrastructure.Storage;

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
            var storage = new LinuxStorageService(ssh);
            var files = new SftpRemoteFileService();
            var preflight = new DeploymentPreflightService(ssh);
            var deployment = new ComposeDeploymentService(ssh);

            var viewModel = new MainWindowViewModel(probe, store, bootstrap);
            viewModel.InitializeContainers(containers);
            viewModel.InitializeDeployments(preflight, deployment);
            viewModel.InitializeLogs(containers, logs);
            viewModel.InitializeStorage(storage);
            viewModel.InitializeFiles(files);

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
