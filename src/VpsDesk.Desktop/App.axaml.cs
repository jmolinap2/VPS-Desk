using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using VpsDesk.Application.Deployments;
using VpsDesk.Desktop.Localization;
using VpsDesk.Desktop.Services;
using VpsDesk.Desktop.ViewModels;
using VpsDesk.Desktop.Views;
using VpsDesk.Infrastructure.Docker;
using VpsDesk.Infrastructure.Logs;
using VpsDesk.Infrastructure.Monitoring;
using VpsDesk.Infrastructure.Security;
using VpsDesk.Infrastructure.Ssh;
using VpsDesk.Infrastructure.Storage;

namespace VpsDesk.Desktop;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        LocalizationService.Current.Initialize(this);
    }

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
            var discovery = new DeploymentDiscoveryService(ssh);
            var postDeployVerification = new PostDeployVerificationService(ssh);
            var security = new LinuxSecurityAuditService(ssh);

            var viewModel = new MainWindowViewModel(probe, store, bootstrap);
            LocalizationService.Current.CultureChanged += (_, _) => viewModel.RefreshLocalization();
            viewModel.InitializeContainers(containers);
            viewModel.InitializeDeployments(
                preflight,
                deployment,
                discovery,
                postDeployVerification,
                bootstrap.RemoteRepositoryPath,
                bootstrap.ComposeFile);
            viewModel.InitializeLogs(containers, logs);
            viewModel.InitializeStorage(storage);
            viewModel.InitializeFiles(files);
            viewModel.InitializeSecurity(security);

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
