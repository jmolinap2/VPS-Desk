using System.ComponentModel;
using VpsDesk.Application.Activity;
using VpsDesk.Application.Deployments;
using VpsDesk.Desktop.Services;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private DeploymentsViewModel? _deploymentsModule;

    public DeploymentsViewModel DeploymentsModule
        => _deploymentsModule ?? throw new InvalidOperationException("Deployments module has not been initialized.");

    public bool IsDeploymentsPage => SelectedPage == "Deployments";
    public bool IsPendingOperationsPage => !IsDashboardPage
                                           && !IsServersPage
                                           && !IsContainersPage
                                           && !IsProjectsPage
                                           && !IsDeploymentsPage
                                           && !IsEnvironmentPage
                                           && !IsLogsPage
                                           && !IsStoragePage
                                           && !IsFilesPage
                                           && !IsTerminalPage
                                           && !IsSecurityPage
                                           && !IsSettingsPage;

    public void InitializeDeployments(
        IDeploymentPreflightService preflightService,
        IComposeDeploymentService deploymentService,
        IDeploymentDiscoveryService discoveryService,
        IPostDeployVerificationService postDeployVerificationService,
        DeploymentProfileStore deploymentProfileStore,
        IOperationHistoryStore historyStore,
        ServerProfile? bootstrapServer,
        string? remoteRepositoryPath,
        string? composeFile)
    {
        if (_deploymentsModule != null) return;

        _deploymentsModule = new DeploymentsViewModel(
            preflightService,
            deploymentService,
            discoveryService,
            postDeployVerificationService,
            () => _server,
            () => _activeSecret,
            deploymentProfileStore,
            bootstrapServer,
            remoteRepositoryPath,
            composeFile);

        _deploymentsModule.InitializeHistory(historyStore);
        _deploymentsModule.HistoryChanged += async (_, _) => await RefreshRecentActivityAsync();
        _deploymentsModule.LoadForServer(_server);

        PropertyChanged += HandleDeploymentsNavigation;
        OnPropertyChanged(nameof(DeploymentsModule));
        OnPropertyChanged(nameof(IsDeploymentsPage));
        OnPropertyChanged(nameof(IsPendingOperationsPage));
    }

    private void HandleDeploymentsNavigation(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectedPage))
        {
            OnPropertyChanged(nameof(IsDeploymentsPage));
            OnPropertyChanged(nameof(IsPendingOperationsPage));

            if (SelectedPage == "Deployments")
            {
                // Project activation already loaded the correct path/profile. Do not fall back to
                // the old server-only profile when simply navigating between pages.
                if (_activeProject is null)
                {
                    _deploymentsModule?.LoadForServer(_server);
                }
                _ = _deploymentsModule?.TryAutoDiscoverRepositoryAsync();
            }
            else if (SelectedPage == "Dashboard")
            {
                _ = RefreshRecentActivityAsync();
            }
        }
        else if (e.PropertyName == nameof(SelectedServerName))
        {
            _deploymentsModule?.LoadForServer(_server);
            _ = RefreshRecentActivityAsync();
            if (SelectedPage == "Deployments" && _activeProject is null)
            {
                _ = _deploymentsModule?.TryAutoDiscoverRepositoryAsync();
            }
        }
    }
}
