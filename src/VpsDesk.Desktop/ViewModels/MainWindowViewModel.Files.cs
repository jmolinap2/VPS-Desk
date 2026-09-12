using System.ComponentModel;
using VpsDesk.Application.Abstractions;

namespace VpsDesk.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private FilesViewModel? _filesModule;

    public FilesViewModel FilesModule
        => _filesModule ?? throw new InvalidOperationException("Files module has not been initialized.");

    public bool IsFilesPage => SelectedPage == "Files";
    public bool IsPendingModulePage => !IsDashboardPage && !IsServersPage && !IsContainersPage && !IsLogsPage && !IsStoragePage && !IsFilesPage;

    public void InitializeFiles(IRemoteFileService remoteFileService)
    {
        if (_filesModule != null) return;

        _filesModule = new FilesViewModel(
            remoteFileService,
            () => _server,
            () => _activeSecret);

        PropertyChanged += HandleFilesNavigation;
        OnPropertyChanged(nameof(FilesModule));
        OnPropertyChanged(nameof(IsFilesPage));
        OnPropertyChanged(nameof(IsPendingModulePage));
    }

    private void HandleFilesNavigation(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectedPage))
        {
            OnPropertyChanged(nameof(IsFilesPage));
            OnPropertyChanged(nameof(IsPendingModulePage));

            if (IsFilesPage && _filesModule != null)
            {
                _ = _filesModule.RefreshIfNeededAsync();
            }
        }
        else if (e.PropertyName == nameof(SelectedServerName))
        {
            _filesModule?.Reset();
        }
    }
}
