using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VpsDesk.Application.Deployments;
using VpsDesk.Desktop.Services;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Desktop.ViewModels;

public partial class DeploymentsViewModel
{
    private IProjectRecipeService? _recipeService;
    private ProjectRecipe? _activeRecipe;
    private bool _applyingProjectModel;
    private string? _preferredTargetId;
    private string? _preferredMigrationModeId;
    private bool? _preferredRunMigrations;
    private bool? _preferredMigrationOnly;

    public ObservableCollection<ProjectDeploymentTarget> AvailableTargets { get; } = new();
    public ObservableCollection<ProjectMigrationMode> MigrationModes { get; } = new();

    [ObservableProperty] private ProjectDeploymentTarget? _selectedDeploymentTarget;
    [ObservableProperty] private ProjectMigrationMode? _selectedMigrationMode;
    [ObservableProperty] private bool _runMigrations;
    [ObservableProperty] private bool _migrationOnly;
    [ObservableProperty] private string _migrationInput = string.Empty;
    [ObservableProperty] private bool _hasProjectRecipe;
    [ObservableProperty] private bool _hasMigrationRecipe;
    [ObservableProperty] private bool _isProjectRecipeInvalid;
    [ObservableProperty] private string _projectModelStatus = "Modo genérico Docker Compose";
    [ObservableProperty] private string _projectRecipeFileName = string.Empty;

    public bool ShowDeploymentTarget => !MigrationOnly;
    public bool ShowMigrationOptions => HasMigrationRecipe && (MigrationOnly || RunMigrations);
    public bool RequiresMigrationInput => ShowMigrationOptions && SelectedMigrationMode?.Input is not null;
    public string MigrationInputLabel => SelectedMigrationMode?.Input?.Label ?? "Valor";
    public string MigrationInputPlaceholder => SelectedMigrationMode?.Input?.Placeholder ?? string.Empty;
    public string SelectedTargetLabel => MigrationOnly
        ? "Solo migraciones"
        : SelectedDeploymentTarget?.Label ?? "Proyecto Compose";
    public string SelectedMigrationLabel => ShouldExecuteMigrations
        ? SelectedMigrationMode?.Label ?? "Migraciones"
        : "Sin migraciones";

    private bool ShouldExecuteMigrations => HasMigrationRecipe && (MigrationOnly || RunMigrations);

    public void InitializeProjectRecipes(IProjectRecipeService recipeService)
        => _recipeService = recipeService;

    partial void OnSelectedDeploymentTargetChanged(ProjectDeploymentTarget? value)
    {
        OnPropertyChanged(nameof(SelectedTargetLabel));
        if (_applyingProjectModel) return;

        if (value is not null && HasMigrationRecipe && !MigrationOnly)
        {
            RunMigrations = value.RunMigrationsByDefault;
        }
        InvalidatePreflight();
    }

    partial void OnSelectedMigrationModeChanged(ProjectMigrationMode? value)
    {
        MigrationInput = string.Empty;
        NotifyMigrationPresentationChanged();
        if (!_applyingProjectModel) InvalidatePreflight();
    }

    partial void OnRunMigrationsChanged(bool value)
    {
        NotifyMigrationPresentationChanged();
        if (!_applyingProjectModel) InvalidatePreflight();
    }

    partial void OnMigrationOnlyChanged(bool value)
    {
        if (value && HasMigrationRecipe)
        {
            RunMigrations = true;
        }
        OnPropertyChanged(nameof(ShowDeploymentTarget));
        OnPropertyChanged(nameof(SelectedTargetLabel));
        NotifyMigrationPresentationChanged();
        if (!_applyingProjectModel) InvalidatePreflight();
    }

    partial void OnMigrationInputChanged(string value)
    {
        if (!_applyingProjectModel) InvalidatePreflight();
    }

    private void NotifyMigrationPresentationChanged()
    {
        OnPropertyChanged(nameof(ShowMigrationOptions));
        OnPropertyChanged(nameof(RequiresMigrationInput));
        OnPropertyChanged(nameof(MigrationInputLabel));
        OnPropertyChanged(nameof(MigrationInputPlaceholder));
        OnPropertyChanged(nameof(SelectedMigrationLabel));
    }

    private void LoadProjectPreferences(DeploymentProfile? profile)
    {
        _preferredTargetId = profile?.DeploymentTargetId;
        _preferredMigrationModeId = profile?.MigrationModeId;
        _preferredRunMigrations = profile?.RunMigrations;
        _preferredMigrationOnly = profile?.MigrationOnly;
        ResetProjectModel();
    }

    private void ResetProjectModel()
    {
        _applyingProjectModel = true;
        try
        {
            _activeRecipe = null;
            AvailableTargets.Clear();
            MigrationModes.Clear();
            SelectedDeploymentTarget = null;
            SelectedMigrationMode = null;
            RunMigrations = false;
            MigrationOnly = false;
            MigrationInput = string.Empty;
            HasProjectRecipe = false;
            HasMigrationRecipe = false;
            IsProjectRecipeInvalid = false;
            ProjectRecipeFileName = string.Empty;
            ProjectModelStatus = "Modo genérico Docker Compose";
            NotifyMigrationPresentationChanged();
            OnPropertyChanged(nameof(ShowDeploymentTarget));
            OnPropertyChanged(nameof(SelectedTargetLabel));
        }
        finally
        {
            _applyingProjectModel = false;
        }
    }

    private async Task RefreshProjectModelAsync(ServerProfile server)
    {
        if (string.IsNullOrWhiteSpace(RemoteRepositoryPath) || string.IsNullOrWhiteSpace(ComposeFile))
        {
            ResetProjectModel();
            return;
        }

        if (_recipeService is null)
        {
            ResetProjectModel();
            ProjectModelStatus = "El servicio de recetas de proyecto no está inicializado.";
            IsProjectRecipeInvalid = true;
            return;
        }

        IReadOnlyList<string> composeServices = [];
        string? serviceDiscoveryError = null;
        try
        {
            composeServices = await _discovery.DiscoverServicesAsync(
                server,
                RemoteRepositoryPath.Trim(),
                ComposeFile.Trim(),
                _secretAccessor());
        }
        catch (Exception ex)
        {
            serviceDiscoveryError = ex.Message;
        }

        var recipeResult = await _recipeService.LoadAsync(
            server,
            RemoteRepositoryPath.Trim(),
            _secretAccessor());

        _applyingProjectModel = true;
        try
        {
            _activeRecipe = null;
            AvailableTargets.Clear();
            MigrationModes.Clear();
            SelectedDeploymentTarget = null;
            SelectedMigrationMode = null;
            MigrationInput = string.Empty;
            HasProjectRecipe = false;
            HasMigrationRecipe = false;
            IsProjectRecipeInvalid = false;
            ProjectRecipeFileName = recipeResult.FileName ?? string.Empty;

            if (recipeResult.Status == ProjectRecipeStatus.Invalid)
            {
                IsProjectRecipeInvalid = true;
                ProjectModelStatus = $"Receta de proyecto inválida: {recipeResult.Error}";
                RunMigrations = false;
                MigrationOnly = false;
                return;
            }

            if (recipeResult.Status == ProjectRecipeStatus.Loaded && recipeResult.Recipe is not null)
            {
                _activeRecipe = recipeResult.Recipe;
                HasProjectRecipe = true;
                HasMigrationRecipe = _activeRecipe.Migrations is not null;
                ProjectModelStatus = $"{_activeRecipe.Name} · receta {_activeRecipe.Version} · {recipeResult.FileName}";

                foreach (var target in _activeRecipe.Deploy.Targets)
                {
                    AvailableTargets.Add(target);
                }

                var preferredTarget = FindTarget(_preferredTargetId)
                                      ?? FindTarget(_activeRecipe.Deploy.DefaultTarget)
                                      ?? AvailableTargets.FirstOrDefault();
                SelectedDeploymentTarget = preferredTarget;

                if (_activeRecipe.Migrations is { } migrations)
                {
                    foreach (var mode in migrations.Modes)
                    {
                        MigrationModes.Add(mode);
                    }

                    SelectedMigrationMode = FindMigrationMode(_preferredMigrationModeId)
                                            ?? FindMigrationMode(migrations.DefaultMode)
                                            ?? MigrationModes.FirstOrDefault();
                    MigrationOnly = _preferredMigrationOnly ?? false;
                    RunMigrations = MigrationOnly
                        || (_preferredRunMigrations
                            ?? SelectedDeploymentTarget?.RunMigrationsByDefault
                            ?? true);
                }
                else
                {
                    MigrationOnly = false;
                    RunMigrations = false;
                }
            }
            else
            {
                BuildGenericTargets(composeServices);
                ProjectModelStatus = serviceDiscoveryError is null
                    ? $"Modo genérico Docker Compose · {composeServices.Count} servicio(s) detectado(s)"
                    : $"Modo genérico Docker Compose · no se pudieron enumerar servicios: {serviceDiscoveryError}";
                MigrationOnly = false;
                RunMigrations = false;
            }
        }
        finally
        {
            _applyingProjectModel = false;
            NotifyMigrationPresentationChanged();
            OnPropertyChanged(nameof(ShowDeploymentTarget));
            OnPropertyChanged(nameof(SelectedTargetLabel));
            InvalidatePreflight();
        }
    }

    private void BuildGenericTargets(IReadOnlyList<string> services)
    {
        var all = services
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        AvailableTargets.Add(new ProjectDeploymentTarget(
            "all",
            all.Length == 0 ? "Proyecto Compose completo" : "Todos los servicios",
            all,
            false,
            false));

        foreach (var service in all)
        {
            AvailableTargets.Add(new ProjectDeploymentTarget(
                service,
                service,
                [service],
                false,
                false));
        }

        SelectedDeploymentTarget = FindTarget(_preferredTargetId) ?? AvailableTargets.FirstOrDefault();
    }

    private ProjectDeploymentTarget? FindTarget(string? id)
        => string.IsNullOrWhiteSpace(id)
            ? null
            : AvailableTargets.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private ProjectMigrationMode? FindMigrationMode(string? id)
        => string.IsNullOrWhiteSpace(id)
            ? null
            : MigrationModes.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private IReadOnlyList<string> GetRequiredServices()
    {
        var services = new List<string>();
        if (!MigrationOnly && SelectedDeploymentTarget is not null)
        {
            services.AddRange(SelectedDeploymentTarget.Services);
        }

        if (ShouldExecuteMigrations && _activeRecipe?.Migrations is { } migrations)
        {
            services.AddRange(migrations.RequiredServices);
        }

        return services
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<string> GetDeploymentServices()
        => MigrationOnly
            ? []
            : SelectedDeploymentTarget?.Services ?? [];

    private DeploymentProjectOperationRequest? BuildProjectOperation()
    {
        if (!ShouldExecuteMigrations || SelectedMigrationMode is null) return null;

        return new DeploymentProjectOperationRequest(
            "migrations",
            $"{_activeRecipe?.Migrations?.Label ?? "Migraciones"} · {SelectedMigrationMode.Label}",
            SelectedMigrationMode.Command,
            RequiresMigrationInput ? MigrationInput.Trim() : null,
            900,
            true);
    }

    private string BuildExecutionSummary()
    {
        var action = MigrationOnly
            ? "Solo migraciones"
            : $"Objetivo: {SelectedDeploymentTarget?.Label ?? "Compose completo"}";
        var migrations = ShouldExecuteMigrations
            ? $"Migraciones: {SelectedMigrationMode?.Label ?? "configuradas"}"
            : "Migraciones: no";
        return $"{action} · {migrations}";
    }
}
