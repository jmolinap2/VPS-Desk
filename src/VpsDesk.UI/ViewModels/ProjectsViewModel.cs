using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Application.Deployments;
using VpsDesk.Desktop.Services;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Desktop.ViewModels;

public sealed record ProjectDiscoveryItem(
    string Name,
    string RemoteRepositoryPath,
    string Branch,
    string ComposeFile,
    bool HasRecipe,
    bool RecipeInvalid,
    string? RecipeFileName,
    int TargetCount,
    bool HasMigrations,
    bool AlreadyAssociated)
{
    public string ModeLabel => RecipeInvalid
        ? "Integración VPS Desk inválida"
        : HasRecipe
            ? "Integración VPS Desk detectada"
            : "Modo estándar Docker Compose";

    public string CapabilitySummary => RecipeInvalid
        ? "Revisa .vpsdesk.yml antes de ejecutar operaciones"
        : HasRecipe
            ? $"{TargetCount} objetivo(s)" + (HasMigrations ? " · Migraciones disponibles" : string.Empty)
            : "Git · Docker Compose";
}

public partial class ProjectsViewModel : ObservableObject
{
    private readonly IDeploymentDiscoveryService _discovery;
    private readonly IProjectRecipeService _recipes;
    private readonly ProjectWorkspaceStore _store;
    private readonly DeploymentProfileStore _deploymentProfiles;
    private readonly Func<ServerProfile?> _serverAccessor;
    private readonly Func<string?> _secretAccessor;
    private readonly Func<ProjectWorkspace?, Task> _activateProject;
    private bool _suppressSelection;

    public ObservableCollection<ProjectWorkspace> AssociatedProjects { get; } = new();
    public ObservableCollection<ProjectDiscoveryItem> DiscoveredProjects { get; } = new();

    [ObservableProperty] private ProjectWorkspace? _selectedProject;
    [ObservableProperty] private ProjectDiscoveryItem? _selectedDiscovery;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Conecta un servidor para descubrir sus proyectos Git + Docker Compose.";

    public bool HasProjects => AssociatedProjects.Count > 0;
    public bool HasActiveProject => SelectedProject is not null;
    public bool HasDiscoveredProjects => DiscoveredProjects.Count > 0;
    public string ActiveProjectName => SelectedProject?.Name ?? "Sin proyecto activo";
    public string ActiveProjectPath => SelectedProject?.RemoteRepositoryPath ?? "Selecciona o asocia un proyecto";
    public string ActiveProjectMode => SelectedProject?.ModeLabel ?? "Sin asociación";

    public ProjectsViewModel(
        IDeploymentDiscoveryService discovery,
        IProjectRecipeService recipes,
        ProjectWorkspaceStore store,
        DeploymentProfileStore deploymentProfiles,
        Func<ServerProfile?> serverAccessor,
        Func<string?> secretAccessor,
        Func<ProjectWorkspace?, Task> activateProject)
    {
        _discovery = discovery;
        _recipes = recipes;
        _store = store;
        _deploymentProfiles = deploymentProfiles;
        _serverAccessor = serverAccessor;
        _secretAccessor = secretAccessor;
        _activateProject = activateProject;
    }

    partial void OnSelectedProjectChanged(ProjectWorkspace? value)
    {
        NotifyProjectState();
        if (_suppressSelection) return;
        _ = ActivateSelectionAsync(value);
    }

    public void LoadForServer()
    {
        var server = _serverAccessor();
        _suppressSelection = true;
        try
        {
            AssociatedProjects.Clear();
            DiscoveredProjects.Clear();
            SelectedDiscovery = null;

            if (server is null)
            {
                SelectedProject = null;
                StatusMessage = "Selecciona y activa un servidor antes de administrar proyectos.";
                return;
            }

            // Backward-compatible migration: preserve the deployment path/options that existed
            // before Project became a first-class concept. The old record is promoted to the
            // project id instead of copied, so removing a project later cannot resurrect it.
            var legacyProfile = _deploymentProfiles.Find(server.Id);
            var imported = _store.ImportLegacyProfile(legacyProfile);
            if (legacyProfile?.ProjectId is null && imported is not null)
            {
                _deploymentProfiles.PromoteLegacyProfile(
                    server.Id,
                    imported.RemoteRepositoryPath,
                    imported.Id);
            }

            foreach (var project in _store.List(server.Id)) AssociatedProjects.Add(project);

            SelectedProject = _store.GetSelected(server.Id)
                              ?? imported
                              ?? AssociatedProjects.FirstOrDefault();

            StatusMessage = AssociatedProjects.Count == 0
                ? "Aún no hay proyectos asociados. Pulsa Detectar proyectos; VPS Desk buscará Git + Docker Compose sin instalar nada en el servidor."
                : $"{AssociatedProjects.Count} proyecto(s) asociado(s) a {server.Name}. Puedes cambiar el proyecto activo desde aquí o desde el selector lateral.";
        }
        finally
        {
            _suppressSelection = false;
            NotifyProjectState();
        }

        if (SelectedProject is not null)
        {
            _ = ActivateSelectionAsync(SelectedProject);
        }
    }

    [RelayCommand]
    public async Task DiscoverAsync()
    {
        if (IsBusy) return;
        var server = _serverAccessor();
        if (server is null)
        {
            StatusMessage = "No hay un servidor activo. Selecciónalo primero en Servidores.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Buscando proyectos Git con Docker Compose en /root, /home, /opt y /srv...";
        DiscoveredProjects.Clear();
        SelectedDiscovery = null;

        try
        {
            var candidates = await _discovery.DiscoverProjectsAsync(server, _secretAccessor());
            foreach (var candidate in candidates)
            {
                var existing = _store.FindByPath(server.Id, candidate.RemoteRepositoryPath);
                var detail = await _discovery.DiscoverAsync(
                    server,
                    candidate.RemoteRepositoryPath,
                    _secretAccessor());

                var compose = existing?.ComposeFile;
                if (string.IsNullOrWhiteSpace(compose)
                    || !candidate.ComposeFiles.Contains(compose, StringComparer.OrdinalIgnoreCase))
                {
                    compose = candidate.ComposeFiles.FirstOrDefault() ?? "docker-compose.yml";
                }

                var branch = existing?.Branch;
                if (string.IsNullOrWhiteSpace(branch))
                {
                    branch = detail.CurrentBranch
                             ?? detail.Branches.FirstOrDefault()
                             ?? "main";
                }

                var recipe = await _recipes.LoadAsync(
                    server,
                    candidate.RemoteRepositoryPath,
                    _secretAccessor());

                var hasRecipe = recipe.Status == ProjectRecipeStatus.Loaded && recipe.Recipe is not null;
                var recipeInvalid = recipe.Status == ProjectRecipeStatus.Invalid;
                var name = hasRecipe
                    ? recipe.Recipe!.Name
                    : existing?.Name ?? ProjectNameFromPath(candidate.RemoteRepositoryPath);

                DiscoveredProjects.Add(new ProjectDiscoveryItem(
                    name,
                    candidate.RemoteRepositoryPath,
                    branch,
                    compose,
                    hasRecipe,
                    recipeInvalid,
                    recipe.FileName,
                    hasRecipe ? recipe.Recipe!.Deploy.Targets.Count : 0,
                    hasRecipe && recipe.Recipe!.Migrations is not null,
                    existing is not null));
            }

            SelectedDiscovery = DiscoveredProjects.FirstOrDefault(item => !item.AlreadyAssociated)
                                ?? DiscoveredProjects.FirstOrDefault();
            StatusMessage = DiscoveredProjects.Count switch
            {
                0 => "No encontré proyectos Compose en las rutas convencionales. Puedes seguir usando Despliegues e indicar una ruta manualmente.",
                1 => "Encontré 1 proyecto. Revísalo y asócialo para que VPS Desk recuerde su contexto.",
                _ => $"Encontré {DiscoveredProjects.Count} proyectos. Asocia los que quieras administrar; cada uno conserva su propia configuración."
            };
        }
        catch (Exception ex)
        {
            StatusMessage = $"No se pudieron detectar proyectos: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            NotifyProjectState();
        }
    }

    [RelayCommand]
    private async Task AssociateSelectedAsync()
    {
        var server = _serverAccessor();
        var item = SelectedDiscovery;
        if (server is null || item is null || IsBusy) return;

        var existing = _store.FindByPath(server.Id, item.RemoteRepositoryPath);
        var workspace = new ProjectWorkspace(
            existing?.Id ?? Guid.NewGuid(),
            server.Id,
            item.Name,
            item.RemoteRepositoryPath,
            item.Branch,
            item.ComposeFile,
            existing?.EnvironmentFileName ?? ".env",
            item.HasRecipe,
            item.RecipeFileName,
            item.TargetCount,
            item.HasMigrations,
            DateTimeOffset.UtcNow);

        _store.Upsert(workspace, select: true);
        _deploymentProfiles.PromoteLegacyProfile(server.Id, workspace.RemoteRepositoryPath, workspace.Id);
        ReloadAssociated(server.Id, workspace.Id);
        StatusMessage = item.AlreadyAssociated
            ? $"Proyecto '{workspace.Name}' actualizado y seleccionado."
            : $"Proyecto '{workspace.Name}' asociado a {server.Name} y seleccionado.";
        await _activateProject(SelectedProject);
    }

    [RelayCommand]
    private async Task RemoveSelectedAsync()
    {
        var server = _serverAccessor();
        var selected = SelectedProject;
        if (server is null || selected is null || IsBusy) return;

        _store.Remove(selected.Id);
        _deploymentProfiles.RemoveProject(server.Id, selected.Id);
        ReloadAssociated(server.Id, _store.GetSelected(server.Id)?.Id);
        StatusMessage = $"Se quitó la asociación de '{selected.Name}'. No se modificó ningún archivo ni contenedor del VPS.";
        await _activateProject(SelectedProject);
    }

    [RelayCommand]
    private async Task ActivateSelectedAsync()
    {
        if (SelectedProject is null) return;
        await ActivateSelectionAsync(SelectedProject);
    }

    private async Task ActivateSelectionAsync(ProjectWorkspace? project)
    {
        var server = _serverAccessor();
        if (server is null) return;
        if (project is not null)
        {
            _store.Select(server.Id, project.Id);
            StatusMessage = $"Proyecto activo: {project.Name} · {project.RemoteRepositoryPath}";
        }
        await _activateProject(project);
    }

    private void ReloadAssociated(Guid serverId, Guid? preferredId)
    {
        _suppressSelection = true;
        try
        {
            AssociatedProjects.Clear();
            foreach (var project in _store.List(serverId)) AssociatedProjects.Add(project);
            SelectedProject = preferredId is Guid id
                ? AssociatedProjects.FirstOrDefault(project => project.Id == id)
                : AssociatedProjects.FirstOrDefault();
        }
        finally
        {
            _suppressSelection = false;
            NotifyProjectState();
        }
    }

    private void NotifyProjectState()
    {
        OnPropertyChanged(nameof(HasProjects));
        OnPropertyChanged(nameof(HasActiveProject));
        OnPropertyChanged(nameof(HasDiscoveredProjects));
        OnPropertyChanged(nameof(ActiveProjectName));
        OnPropertyChanged(nameof(ActiveProjectPath));
        OnPropertyChanged(nameof(ActiveProjectMode));
    }

    private static string ProjectNameFromPath(string path)
        => path.Trim().TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
           ?? "Proyecto";
}
