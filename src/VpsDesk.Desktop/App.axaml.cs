using System.Globalization;
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
using VpsDesk.Infrastructure.Persistence;
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
            var appSettingsStore = new AppSettingsStore();
            var appSettings = appSettingsStore.Load();
            ApplyLanguagePreference(appSettings.Language);

            var bootstrap = BootstrapServerProfileLoader.Load();
            var store = new ServerProfileStore();
            var deploymentProfileStore = new DeploymentProfileStore();
            var operationHistoryStore = new SqliteOperationHistoryStore();
            var ssh = new SshNetCommandExecutor();
            var terminal = new SshNetInteractiveTerminalService();
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
            viewModel.InitializeNavigationState();
            LocalizationService.Current.CultureChanged += (_, _) => viewModel.RefreshLocalization();
            viewModel.InitializeActivity(operationHistoryStore);
            viewModel.InitializeContainers(containers);
            viewModel.InitializeDeployments(
                preflight,
                deployment,
                discovery,
                postDeployVerification,
                deploymentProfileStore,
                operationHistoryStore,
                bootstrap.Profile,
                bootstrap.RemoteRepositoryPath,
                bootstrap.ComposeFile);
            viewModel.InitializeLogs(containers, logs);
            viewModel.InitializeStorage(storage);
            viewModel.InitializeFiles(files);
            viewModel.InitializeSecurity(security);
            viewModel.InitializeTerminal(terminal);
            viewModel.InitializeSettings(appSettingsStore, appSettings, ApplyLanguagePreference);

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ApplyLanguagePreference(string preference)
    {
        var culture = preference switch
        {
            "es-ES" => "es-ES",
            "en-US" => "en-US",
            _ => CultureInfo.InstalledUICulture.Name
        };
        LocalizationService.Current.ApplyCulture(this, culture);
    }
}
