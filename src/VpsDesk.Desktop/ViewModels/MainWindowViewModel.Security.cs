using System.ComponentModel;
using VpsDesk.Application.Abstractions;

namespace VpsDesk.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private SecurityViewModel? _securityModule;

    public SecurityViewModel SecurityModule
        => _securityModule ?? throw new InvalidOperationException("Security module has not been initialized.");

    public bool IsSecurityPage => SelectedPage == "Security";
    public bool IsPendingUtilityPage => !IsDashboardPage && !IsServersPage && !IsContainersPage && !IsDeploymentsPage && !IsLogsPage && !IsStoragePage && !IsFilesPage && !IsSecurityPage;

    public void InitializeSecurity(ISecurityAuditService securityAuditService)
    {
        if (_securityModule != null) return;

        _securityModule = new SecurityViewModel(
            securityAuditService,
            () => _server,
            () => _activeSecret,
            _operationHistoryStore);
        _securityModule.HistoryChanged += async (_, _) => await RefreshRecentActivityAsync();

        PropertyChanged += HandleSecurityNavigation;
        OnPropertyChanged(nameof(SecurityModule));
        OnPropertyChanged(nameof(IsSecurityPage));
        OnPropertyChanged(nameof(IsPendingUtilityPage));
    }

    private void HandleSecurityNavigation(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectedPage))
        {
            OnPropertyChanged(nameof(IsSecurityPage));
            OnPropertyChanged(nameof(IsPendingUtilityPage));

            if (IsSecurityPage && _securityModule != null)
            {
                _ = _securityModule.AuditIfNeededAsync();
            }
        }
        else if (e.PropertyName == nameof(SelectedServerName))
        {
            _securityModule?.Reset();
        }
    }
}
