using System.ComponentModel;
using VpsDesk.Application.Abstractions;

namespace VpsDesk.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private StorageViewModel? _storageModule;

    public StorageViewModel StorageModule
        => _storageModule ?? throw new InvalidOperationException("Storage module has not been initialized.");

    public bool IsStoragePage => SelectedPage == "Storage";
    public bool IsRemainingPlaceholderPage => !IsDashboardPage && !IsServersPage && !IsContainersPage && !IsLogsPage && !IsStoragePage;

    public void InitializeStorage(IStorageService storageService)
    {
        if (_storageModule != null) return;

        _storageModule = new StorageViewModel(
            storageService,
            () => _server,
            () => _activeSecret);

        PropertyChanged += HandleStorageNavigation;
        OnPropertyChanged(nameof(StorageModule));
        OnPropertyChanged(nameof(IsStoragePage));
        OnPropertyChanged(nameof(IsRemainingPlaceholderPage));
    }

    private void HandleStorageNavigation(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectedPage))
        {
            OnPropertyChanged(nameof(IsStoragePage));
            OnPropertyChanged(nameof(IsRemainingPlaceholderPage));

            if (IsStoragePage && _storageModule != null)
            {
                _ = _storageModule.RefreshIfNeededAsync();
            }
        }
        else if (e.PropertyName == nameof(SelectedServerName))
        {
            _storageModule?.Reset();
        }
    }
}
