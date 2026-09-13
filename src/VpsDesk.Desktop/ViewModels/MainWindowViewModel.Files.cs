using System.ComponentModel;
using VpsDesk.Application.Abstractions;

namespace VpsDesk.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private FilesViewModel? _filesModule;

    public FilesViewModel FilesModule
        => _filesModule ?? throw new InvalidOperationException("Files module has not been initialized.");

    public bool IsFilesPage => SelectedPage == "Files";
    public bool IsEnvironmentPage => SelectedPage == "Environment";
    public bool IsFileWorkspacePage => IsFilesPage || IsEnvironmentPage;
    public bool IsPendingModulePage => !IsDashboardPage && !IsServersPage && !IsContainersPage && !IsLogsPage && !IsStoragePage && !IsFilesPage && !IsEnvironmentPage;

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
        OnPropertyChanged(nameof(IsEnvironmentPage));
        OnPropertyChanged(nameof(IsFileWorkspacePage));
        OnPropertyChanged(nameof(IsPendingModulePage));
    }

    private void HandleFilesNavigation(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectedPage))
        {
            OnPropertyChanged(nameof(IsFilesPage));
            OnPropertyChanged(nameof(IsEnvironmentPage));
            OnPropertyChanged(nameof(IsFileWorkspacePage));
            OnPropertyChanged(nameof(IsPendingModulePage));

            if (IsEnvironmentPage && _filesModule != null)
            {
                _ = OpenActiveEnvironmentAsync();
            }
            else if (IsFilesPage && _filesModule != null)
            {
                _ = _filesModule.RefreshIfNeededAsync();
            }
        }
        else if (e.PropertyName == nameof(SelectedServerName))
        {
            _filesModule?.Reset();
            if (IsEnvironmentPage && _filesModule != null)
            {
                _ = OpenActiveEnvironmentAsync();
            }
        }
    }

    private async Task OpenActiveEnvironmentAsync()
    {
        if (_filesModule == null) return;

        var projectPath = _deploymentsModule?.RemoteRepositoryPath ?? string.Empty;
        var environmentFile = _deploymentsModule?.EnvironmentFileName ?? ".env";

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            _filesModule.StatusMessage = "Primero configura o detecta la ruta del proyecto en Despliegues. VPS Desk usará esa ruta para localizar el archivo .env.";
            return;
        }

        await _filesModule.OpenEnvironmentFileAsync(projectPath, environmentFile);
    }
}
