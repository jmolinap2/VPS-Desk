using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VpsDesk.Application.Deployments;
using VpsDesk.Desktop.Services;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Desktop.ViewModels;

public partial class DeploymentsViewModel : ObservableObject
{
    private readonly IDeploymentPreflightService _preflight;
    private readonly IComposeDeploymentService _deployment;
    private readonly IDeploymentDiscoveryService _discovery;
    private readonly IPostDeployVerificationService _postDeployVerification;
    private readonly Func<ServerProfile?> _serverAccessor;
    private readonly Func<string?> _secretAccessor;
    private readonly DeploymentProfileStore _profileStore;
    private readonly ServerProfile? _bootstrapServer;
    private readonly string? _bootstrapRemoteRepositoryPath;
    private readonly string? _bootstrapComposeFile;
    private bool _isLoadingProfile;
    private Guid? _activeProjectId;

    public ObservableCollection<PreflightCheckResult> Checks { get; } = new();
    public ObservableCollection<DeploymentStepResult> Steps { get; } = new();
    public ObservableCollection<PostDeployCheckResult> PostChecks { get; } = new();
    public ObservableCollection<string> AvailableBranches { get; } = new();
    public ObservableCollection<string> AvailableComposeFiles { get; } = new();
    public ObservableCollection<DeploymentProjectCandidate> AvailableProjects { get; } = new();

    [ObservableProperty] private string _remoteRepositoryPath = string.Empty;
    [ObservableProperty] private string _branch = "main";
    [ObservableProperty] private string _composeFile = "docker-compose.yml";
    [ObservableProperty] private string? _selectedBranchSuggestion;
    [ObservableProperty] private string? _selectedComposeSuggestion;
    [ObservableProperty] private DeploymentProjectCandidate? _selectedProjectSuggestion;
    [ObservableProperty] private bool _hasProjectSuggestions;
    [ObservableProperty] private bool _requireEnvironmentFile = true;
    [ObservableProperty] private string _environmentFileName = ".env";
    [ObservableProperty] private bool _pullImages = true;
    [ObservableProperty] private bool _buildImages = true;
    [ObservableProperty] private bool _cleanBuildCache = true;
    [ObservableProperty] private string _httpHealthUrls = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _canDeploy;
    [ObservableProperty] private bool _hasPendingDeploy;
    [ObservableProperty] private string _pendingDeployMessage = string.Empty;
    [ObservableProperty] private string _statusMessage = "Paso 1: conecta al servidor seleccionado y detecta sus ramas y archivos Compose.";
    [ObservableProperty] private string _lastChecked = "Never";
    [ObservableProperty] private string _deploymentOutput = string.Empty;

    public Guid? ActiveProjectId => _activeProjectId;

    public DeploymentsViewModel(
        IDeploymentPreflightService preflight,
        IComposeDeploymentService deployment,
        IDeploymentDiscoveryService discovery,
        IPostDeployVerificationService postDeployVerification,
        Func<ServerProfile?> serverAccessor,
        Func<string?> secretAccessor,
        DeploymentProfileStore profileStore,
        ServerProfile? bootstrapServer,
        string? remoteRepositoryPath = null,
        string? composeFile = null)
    {
        _preflight = preflight;
        _deployment = deployment;
        _discovery = discovery;
        _postDeployVerification = postDeployVerification;
        _serverAccessor = serverAccessor;
        _secretAccessor = secretAccessor;
        _profileStore = profileStore;
        _bootstrapServer = bootstrapServer;
        _bootstrapRemoteRepositoryPath = remoteRepositoryPath?.Trim();
        _bootstrapComposeFile = composeFile?.Trim();
    }

    partial void OnRemoteRepositoryPathChanged(string value)
    {
        if (_isLoadingProfile) return;
        AvailableBranches.Clear();
        AvailableComposeFiles.Clear();
        SelectedBranchSuggestion = null;
        SelectedComposeSuggestion = null;
        ResetProjectModel();
        InvalidatePreflight();
    }

    partial void OnBranchChanged(string value)
    {
        if (!_isLoadingProfile) InvalidatePreflight();
    }

    partial void OnComposeFileChanged(string value)
    {
        if (_isLoadingProfile) return;
        ResetProjectModel();
        InvalidatePreflight();
    }

    partial void OnRequireEnvironmentFileChanged(bool value)
    {
        if (!_isLoadingProfile) InvalidatePreflight();
    }

    partial void OnEnvironmentFileNameChanged(string value)
    {
        if (!_isLoadingProfile) InvalidatePreflight();
    }

    partial void OnSelectedBranchSuggestionChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !string.Equals(Branch, value, StringComparison.Ordinal))
        {
            Branch = value;
        }
    }

    partial void OnSelectedComposeSuggestionChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !string.Equals(ComposeFile, value, StringComparison.Ordinal))
        {
            ComposeFile = value;
        }
    }

    partial void OnSelectedProjectSuggestionChanged(DeploymentProjectCandidate? value)
    {
        if (value is null || _isLoadingProfile) return;

        RemoteRepositoryPath = value.RemoteRepositoryPath;
        ComposeFile = value.ComposeFiles.FirstOrDefault(file =>
                          file.Equals(ComposeFile, StringComparison.OrdinalIgnoreCase))
                      ?? value.ComposeFiles.FirstOrDefault()
                      ?? "docker-compose.yml";
    }

    public void Reset()
    {
        Checks.Clear();
        Steps.Clear();
        PostChecks.Clear();
        AvailableBranches.Clear();
        AvailableComposeFiles.Clear();
        AvailableProjects.Clear();
        SelectedBranchSuggestion = null;
        SelectedComposeSuggestion = null;
        SelectedProjectSuggestion = null;
        HasProjectSuggestions = false;
        CanDeploy = false;
        DeploymentOutput = string.Empty;
        LastChecked = "Never";
        StatusMessage = "Paso 1: conecta al servidor seleccionado y detecta sus ramas y archivos Compose.";
        ResetProjectModel();
        CancelPendingDeploy();
    }

    public void LoadForServer(ServerProfile? server)
    {
        _activeProjectId = null;
        _isLoadingProfile = true;
        try
        {
            Reset();
            var saved = server is null ? null : _profileStore.Find(server.Id);
            var useBootstrap = saved is null && server is not null && IsBootstrapServer(server);

            RemoteRepositoryPath = saved?.RemoteRepositoryPath
                ?? (useBootstrap ? _bootstrapRemoteRepositoryPath : null)
                ?? string.Empty;
            Branch = saved?.Branch ?? "main";
            ComposeFile = saved?.ComposeFile
                ?? (useBootstrap ? _bootstrapComposeFile : null)
                ?? "docker-compose.yml";
            RequireEnvironmentFile = saved?.RequireEnvironmentFile ?? true;
            EnvironmentFileName = saved?.EnvironmentFileName ?? ".env";
            PullImages = saved?.PullImages ?? true;
            BuildImages = saved?.BuildImages ?? true;
            CleanBuildCache = saved?.CleanBuildCache ?? true;
            HttpHealthUrls = string.Empty;
            LoadProjectPreferences(saved);

            StatusMessage = server is null
                ? "Selecciona y activa un servidor para usar despliegues."
                : saved is null
                    ? "Selecciona un proyecto o indica una ruta. VPS Desk también puede detectar proyectos automáticamente."
                    : $"Configuración heredada cargada para {server.Name}. Puedes asociarla como proyecto sin perder sus opciones.";
        }
        finally
        {
            _isLoadingProfile = false;
            OnPropertyChanged(nameof(ActiveProjectId));
        }
    }

    public async Task LoadForProjectAsync(ServerProfile server, ProjectWorkspace project)
    {
        _activeProjectId = project.Id;
        _isLoadingProfile = true;
        try
        {
            Reset();
            var saved = _profileStore.Find(server.Id, project.Id);

            RemoteRepositoryPath = saved?.RemoteRepositoryPath ?? project.RemoteRepositoryPath;
            Branch = saved?.Branch ?? project.Branch;
            ComposeFile = saved?.ComposeFile ?? project.ComposeFile;
            RequireEnvironmentFile = saved?.RequireEnvironmentFile ?? true;
            EnvironmentFileName = saved?.EnvironmentFileName ?? project.EnvironmentFileName;
            PullImages = saved?.PullImages ?? true;
            BuildImages = saved?.BuildImages ?? true;
            CleanBuildCache = saved?.CleanBuildCache ?? true;
            HttpHealthUrls = string.Empty;
            LoadProjectPreferences(saved);
            StatusMessage = $"Proyecto activo: {project.Name}. Detectando rama, Compose y capacidades declaradas por el proyecto...";
        }
        finally
        {
            _isLoadingProfile = false;
            OnPropertyChanged(nameof(ActiveProjectId));
        }

        await DiscoverRemoteOptionsAsync();
    }

    public void RemoveForServer(Guid serverId) => _profileStore.Remove(serverId);

    private bool IsBootstrapServer(ServerProfile server)
        => _bootstrapServer is not null
           && string.Equals(server.Host, _bootstrapServer.Host, StringComparison.OrdinalIgnoreCase)
           && server.Port == _bootstrapServer.Port
           && string.Equals(server.Username, _bootstrapServer.Username, StringComparison.OrdinalIgnoreCase);
}
