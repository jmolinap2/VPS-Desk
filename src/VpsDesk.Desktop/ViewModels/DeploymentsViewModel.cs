using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    [ObservableProperty] private string _httpHealthUrls = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _canDeploy;
    [ObservableProperty] private bool _hasPendingDeploy;
    [ObservableProperty] private string _pendingDeployMessage = string.Empty;
    [ObservableProperty] private string _statusMessage = "Paso 1: conecta al servidor seleccionado y detecta sus ramas y archivos Compose.";
    [ObservableProperty] private string _lastChecked = "Never";
    [ObservableProperty] private string _deploymentOutput = string.Empty;

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
        InvalidatePreflight();
    }

    partial void OnBranchChanged(string value)
    {
        if (!_isLoadingProfile) InvalidatePreflight();
    }

    partial void OnComposeFileChanged(string value)
    {
        if (!_isLoadingProfile) InvalidatePreflight();
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
        CancelPendingDeploy();
    }

    public void LoadForServer(ServerProfile? server)
    {
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
            HttpHealthUrls = string.Empty;

            StatusMessage = server is null
                ? "Selecciona y activa un servidor para usar despliegues."
                : saved is null
                    ? "Configura este servidor una vez; VPS Desk guardará sus opciones de despliegue localmente."
                    : $"Perfil de despliegue cargado para {server.Name}. Detecta o valida antes de desplegar.";
        }
        finally
        {
            _isLoadingProfile = false;
        }
    }

    public void RemoveForServer(Guid serverId) => _profileStore.Remove(serverId);

    public Task TryAutoDiscoverRepositoryAsync()
        => string.IsNullOrWhiteSpace(RemoteRepositoryPath)
            ? DiscoverRemoteOptionsAsync()
            : Task.CompletedTask;

    [RelayCommand]
    public async Task DiscoverRemoteOptionsAsync()
    {
        if (IsBusy) return;
        var server = _serverAccessor();
        if (server == null)
        {
            StatusMessage = "El paso 1 está bloqueado: no hay un servidor activo. Selecciónalo y conéctalo primero en Servidores.";
            return;
        }
        if (string.IsNullOrWhiteSpace(RemoteRepositoryPath))
        {
            await DiscoverProjectsAsync(server);
            return;
        }

        IsBusy = true;
        StatusMessage = "Discovering branches and Docker Compose files on the remote server...";

        try
        {
            var result = await _discovery.DiscoverAsync(
                server,
                RemoteRepositoryPath.Trim(),
                _secretAccessor());

            AvailableBranches.Clear();
            foreach (var branch in result.Branches) AvailableBranches.Add(branch);

            AvailableComposeFiles.Clear();
            foreach (var file in result.ComposeFiles) AvailableComposeFiles.Add(file);

            SelectedBranchSuggestion = result.Branches.FirstOrDefault(x =>
                x.Equals(Branch, StringComparison.OrdinalIgnoreCase));
            SelectedComposeSuggestion = result.ComposeFiles.FirstOrDefault(x =>
                x.Equals(ComposeFile, StringComparison.OrdinalIgnoreCase));

            PersistProfile(server);
            StatusMessage = $"Detección completada: {result.Branches.Count} rama(s) y {result.ComposeFiles.Count} archivo(s) Compose. La configuración quedó guardada para este servidor; valida los requisitos (paso 2).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"La detección falló. Revisa la conexión SSH y la ruta del repositorio: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DiscoverProjectsAsync(ServerProfile server)
    {
        IsBusy = true;
        StatusMessage = "Buscando automáticamente proyectos Git con Docker Compose en el VPS...";

        try
        {
            var projects = await _discovery.DiscoverProjectsAsync(server, _secretAccessor());

            AvailableProjects.Clear();
            foreach (var project in projects) AvailableProjects.Add(project);
            HasProjectSuggestions = AvailableProjects.Count > 0;

            if (projects.Count == 0)
            {
                StatusMessage = "No encontré un proyecto Compose en /root, /home, /opt ni /srv. Indica la ruta una sola vez y se guardará para este VPS.";
                return;
            }

            if (projects.Count > 1)
            {
                StatusMessage = $"Encontré {projects.Count} proyectos Compose. Selecciona el proyecto correcto; la ruta se guardará para este VPS.";
                return;
            }

            SelectedProjectSuggestion = projects[0];
        }
        catch (Exception ex)
        {
            StatusMessage = $"No se pudo detectar automáticamente el proyecto: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        if (!string.IsNullOrWhiteSpace(RemoteRepositoryPath))
        {
            await DiscoverRemoteOptionsAsync();
        }
    }

    [RelayCommand]
    public async Task RunPreflightAsync()
    {
        if (IsBusy) return;
        var server = _serverAccessor();
        if (server == null)
        {
            StatusMessage = "La validación está bloqueada: no hay un servidor activo. Selecciónalo y conéctalo primero en Servidores.";
            return;
        }

        if (!ValidateConfiguration()) return;

        IsBusy = true;
        CanDeploy = false;
        CancelPendingDeploy();
        PostChecks.Clear();
        StatusMessage = "Running remote deployment preflight...";

        try
        {
            var result = await _preflight.CheckAsync(
                new DeploymentPreflightRequest(
                    server,
                    RemoteRepositoryPath.Trim(),
                    Branch.Trim(),
                    ComposeFile.Trim(),
                    RequireEnvironmentFile,
                    EnvironmentFileName.Trim()),
                _secretAccessor());

            Checks.Clear();
            foreach (var check in result.Checks) Checks.Add(check);

            CanDeploy = result.CanProceed;
            LastChecked = DateTimeOffset.Now.ToString("HH:mm:ss");
            if (result.CanProceed)
            {
                PersistProfile(server);
            }
            StatusMessage = result.CanProceed
                ? "La validación pasó. El paso 3 ya está habilitado: solicita el despliegue, revisa la confirmación y ejecútalo."
                : "La validación bloqueó el despliegue. Corrige los requisitos fallidos y repite el paso 2.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Preflight failed: {ex.Message}";
            CanDeploy = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SaveProfile()
    {
        var server = _serverAccessor();
        if (server == null)
        {
            StatusMessage = "Guarda primero y activa un servidor para asociar este perfil de despliegue.";
            return;
        }

        if (!ValidateConfiguration()) return;

        PersistProfile(server);
        StatusMessage = $"Perfil de despliegue guardado para {server.Name}. No se guardaron secretos.";
    }

    [RelayCommand]
    private void RequestDeploy()
    {
        var server = _serverAccessor();
        if (server == null || !CanDeploy)
        {
            StatusMessage = "A successful preflight is required before deployment.";
            return;
        }

        HasPendingDeploy = true;
        var productionWarning = server.Environment == ServerEnvironment.Production
            ? " This is a Production server and containers may be recreated or restarted."
            : " Containers may be recreated or restarted.";
        PendingDeployMessage = $"Deploy branch '{Branch.Trim()}' from '{RemoteRepositoryPath.Trim()}' using '{ComposeFile.Trim()}'.{productionWarning}";
    }

    [RelayCommand]
    private async Task ConfirmDeployAsync()
    {
        if (!HasPendingDeploy || !CanDeploy || IsBusy) return;
        var server = _serverAccessor();
        if (server == null)
        {
            StatusMessage = "The active server changed. Deployment cancelled.";
            CancelPendingDeploy();
            return;
        }

        if (!ValidateConfiguration()) return;

        IsBusy = true;
        CanDeploy = false;
        Steps.Clear();
        PostChecks.Clear();
        DeploymentOutput = string.Empty;
        StatusMessage = "Deployment is running. Do not close VPS Desk until it finishes.";
        CancelPendingDeploy();

        try
        {
            var result = await _deployment.ExecuteAsync(
                new ComposeDeploymentRequest(
                    server,
                    RemoteRepositoryPath.Trim(),
                    Branch.Trim(),
                    ComposeFile.Trim(),
                    PullImages,
                    BuildImages),
                _secretAccessor());

            var output = new StringBuilder();
            foreach (var step in result.Steps)
            {
                Steps.Add(step);
                output.AppendLine($"[{(step.Succeeded ? "OK" : "FAILED")}] {step.Label} · exit {step.ExitCode} · {step.Duration.TotalSeconds:F1}s");
                if (!string.IsNullOrWhiteSpace(step.Output))
                {
                    output.AppendLine(step.Output.TrimEnd());
                }
                output.AppendLine();
            }

            DeploymentOutput = output.ToString().TrimEnd();

            if (!result.Succeeded)
            {
                StatusMessage = $"Deployment stopped at '{result.FailedStep?.Label ?? "unknown step"}'. Review the sanitized output.";
                return;
            }

            StatusMessage = "Deployment commands completed. Verifying the real container and HTTP state...";
            var verification = await _postDeployVerification.VerifyAsync(
                new PostDeployVerificationRequest(
                    server,
                    RemoteRepositoryPath.Trim(),
                    ComposeFile.Trim(),
                    ParseHealthUrls()),
                _secretAccessor());

            foreach (var check in verification.Checks) PostChecks.Add(check);

            var elapsed = (result.FinishedAt - result.StartedAt).TotalSeconds;
            StatusMessage = verification.Passed
                ? verification.HasWarnings
                    ? $"Deployment completed in {elapsed:F1}s. Post-deploy verification passed with warnings."
                    : $"Deployment completed in {elapsed:F1}s and post-deploy verification passed."
                : $"Deployment commands completed in {elapsed:F1}s, but post-deploy verification found problems. Review the checks before considering the release healthy.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Deployment failed or could not be verified: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelDeploy() => CancelPendingDeploy();

    private IReadOnlyList<string> ParseHealthUrls()
        => HttpHealthUrls
            .Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private void InvalidatePreflight()
    {
        if (CanDeploy || Checks.Count > 0)
        {
            CanDeploy = false;
            Checks.Clear();
            StatusMessage = "Deployment settings changed. Run preflight again.";
        }
        CancelPendingDeploy();
    }

    private bool ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(RemoteRepositoryPath))
        {
            StatusMessage = "Remote repository path is required.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(Branch))
        {
            StatusMessage = "Git branch is required.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(ComposeFile))
        {
            StatusMessage = "Compose file is required.";
            return false;
        }
        if (RequireEnvironmentFile && string.IsNullOrWhiteSpace(EnvironmentFileName))
        {
            StatusMessage = "Environment file name is required when the check is enabled.";
            return false;
        }
        return true;
    }

    private void CancelPendingDeploy()
    {
        HasPendingDeploy = false;
        PendingDeployMessage = string.Empty;
    }

    private void PersistProfile(ServerProfile server)
    {
        _profileStore.Upsert(new DeploymentProfile(
            server.Id,
            RemoteRepositoryPath.Trim(),
            Branch.Trim(),
            ComposeFile.Trim(),
            RequireEnvironmentFile,
            EnvironmentFileName.Trim(),
            PullImages,
            BuildImages));
    }

    private bool IsBootstrapServer(ServerProfile server)
        => _bootstrapServer is not null
           && string.Equals(server.Host, _bootstrapServer.Host, StringComparison.OrdinalIgnoreCase)
           && server.Port == _bootstrapServer.Port
           && string.Equals(server.Username, _bootstrapServer.Username, StringComparison.OrdinalIgnoreCase);
}
