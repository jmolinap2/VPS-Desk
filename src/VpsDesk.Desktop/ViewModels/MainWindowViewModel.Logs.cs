using System.ComponentModel;
using VpsDesk.Application.Abstractions;

namespace VpsDesk.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private LogsViewModel? _logsModule;

    public LogsViewModel LogsModule
        => _logsModule ?? throw new InvalidOperationException("Logs module has not been initialized.");

    public bool IsLogsPage => SelectedPage == "Logs";
    public bool IsModulesPlaceholderPage => !IsDashboardPage && !IsServersPage && !IsContainersPage && !IsLogsPage;

    public void InitializeLogs(IContainerService containerService, IRemoteLogService logService)
    {
        if (_logsModule != null) return;

        _logsModule = new LogsViewModel(
            containerService,
            logService,
            () => _server,
            () => _activeSecret);

        PropertyChanged += HandleLogsNavigation;
        OnPropertyChanged(nameof(LogsModule));
        OnPropertyChanged(nameof(IsLogsPage));
        OnPropertyChanged(nameof(IsModulesPlaceholderPage));
    }

    private void HandleLogsNavigation(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectedPage))
        {
            OnPropertyChanged(nameof(IsLogsPage));
            OnPropertyChanged(nameof(IsModulesPlaceholderPage));

            if (IsLogsPage && _logsModule != null)
            {
                _ = _logsModule.RefreshIfNeededAsync();
            }
        }
        else if (e.PropertyName == nameof(SelectedServerName))
        {
            _logsModule?.Reset();
        }
    }
}
