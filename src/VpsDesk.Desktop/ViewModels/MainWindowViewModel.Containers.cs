using System.ComponentModel;
using VpsDesk.Application.Abstractions;

namespace VpsDesk.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private ContainersViewModel? _containerModule;

    public ContainersViewModel ContainerModule
        => _containerModule ?? throw new InvalidOperationException("Containers module has not been initialized.");

    public bool IsContainersPage => SelectedPage == "Containers";
    public bool IsLegacyPlaceholderPage => !IsDashboardPage && !IsServersPage && !IsContainersPage;

    public void InitializeContainers(IContainerService containerService)
    {
        if (_containerModule != null) return;

        _containerModule = new ContainersViewModel(
            containerService,
            () => _server,
            () => _activeSecret);

        PropertyChanged += HandleModuleNavigation;
        OnPropertyChanged(nameof(ContainerModule));
        OnPropertyChanged(nameof(IsContainersPage));
        OnPropertyChanged(nameof(IsLegacyPlaceholderPage));
    }

    private void HandleModuleNavigation(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectedPage))
        {
            OnPropertyChanged(nameof(IsContainersPage));
            OnPropertyChanged(nameof(IsLegacyPlaceholderPage));

            if (IsContainersPage && _containerModule != null)
            {
                _ = _containerModule.RefreshIfNeededAsync();
            }
        }
        else if (e.PropertyName == nameof(SelectedServerName))
        {
            _containerModule?.Reset();
        }
    }
}
