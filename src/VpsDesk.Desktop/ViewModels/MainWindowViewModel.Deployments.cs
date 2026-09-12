using System.ComponentModel;
using VpsDesk.Application.Deployments;

namespace VpsDesk.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private DeploymentsViewModel? _deploymentsModule;

    public DeploymentsViewModel DeploymentsModule
        => _deploymentsModule ?? throw new InvalidOperationException("Deployments module has not been initialized.");

    public bool IsDeploymentsPage => SelectedPage == "Deployments";
    public bool IsPendingOperationsPage => !IsDashboardPage && !IsServersPage && !IsContainersPage && !IsDeploymentsPage && !IsLogsPage && !IsStoragePage && !IsFilesPage;

    public void InitializeDeployments(
        IDeploymentPreflightService preflightService,
        IComposeDeploymentService deploymentService)
    {
        if (_deploymentsModule != null) return;

        _deploymentsModule = new DeploymentsViewModel(
            preflightService,
            deploymentService,
            () => _server,
            () => _activeSecret);

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
        }
        else if (e.PropertyName == nameof(SelectedServerName))
        {
            _deploymentsModule?.Reset();
        }
    }
}
