using System.ComponentModel;
using VpsDesk.Application.Deployments;
using VpsDesk.Desktop.Services;

namespace VpsDesk.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private ProjectsViewModel? _projectsModule;
    private ProjectWorkspace? _activeProject;

    public ProjectsViewModel ProjectsModule
        => _projectsModule ?? throw new InvalidOperationException("Projects module has not been initialized.");

    public bool IsProjectsPage => SelectedPage == "Projects";
    public bool HasActiveProject => _activeProject is not null;
    public string ActiveProjectName => _activeProject?.Name ?? "Sin proyecto activo";
    public string ActiveProjectContext => _activeProject is null
        ? "Asocia un proyecto para habilitar su contexto"
        : $"{_activeProject.Name} · {_activeProject.RemoteRepositoryPath}";
    public string ActiveProjectMode => _activeProject?.ModeLabel ?? "Sin asociación";

    public void InitializeProjects(
        IDeploymentDiscoveryService discoveryService,
        IProjectRecipeService recipeService,
        ProjectWorkspaceStore projectStore,
        DeploymentProfileStore deploymentProfileStore)
    {
        if (_projectsModule is not null) return;

        _projectsModule = new ProjectsViewModel(
            discoveryService,
            recipeService,
            projectStore,
            deploymentProfileStore,
            () => _server,
            () => _activeSecret,
            ActivateProjectContextAsync);

        _projectsModule.LoadForServer();
        PropertyChanged += HandleProjectsNavigation;

        OnPropertyChanged(nameof(ProjectsModule));
        NotifyProjectContextChanged();
    }

    private void HandleProjectsNavigation(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectedPage))
        {
            OnPropertyChanged(nameof(IsProjectsPage));
            if (IsProjectsPage && _projectsModule is not null && !_projectsModule.HasProjects)
            {
                _ = _projectsModule.DiscoverAsync();
            }
            return;
        }

        if (e.PropertyName == nameof(SelectedServerName))
        {
            _activeProject = null;
            _projectsModule?.LoadForServer();
            NotifyProjectContextChanged();
        }
    }

    private async Task ActivateProjectContextAsync(ProjectWorkspace? project)
    {
        _activeProject = project;
        NotifyProjectContextChanged();

        if (_server is not null && _deploymentsModule is not null)
        {
            if (project is null)
            {
                _deploymentsModule.LoadForServer(_server);
            }
            else
            {
                await _deploymentsModule.LoadForProjectAsync(_server, project);
            }
        }

        _filesModule?.Reset();
        if (IsEnvironmentPage && _filesModule is not null)
        {
            await OpenActiveEnvironmentAsync();
        }
    }

    private void NotifyProjectContextChanged()
    {
        OnPropertyChanged(nameof(IsProjectsPage));
        OnPropertyChanged(nameof(HasActiveProject));
        OnPropertyChanged(nameof(ActiveProjectName));
        OnPropertyChanged(nameof(ActiveProjectContext));
        OnPropertyChanged(nameof(ActiveProjectMode));
    }

    private void RemoveProjectsForServer(Guid serverId)
        => _projectsModule?.RemoveForServer(serverId);
}
