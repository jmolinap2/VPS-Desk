using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Application.Deployments;
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

    public ObservableCollection<PreflightCheckResult> Checks { get; } = new();
    public ObservableCollection<DeploymentStepResult> Steps { get; } = new();
    public ObservableCollection<PostDeployCheckResult> PostChecks { get; } = new();
    public ObservableCollection<string> AvailableBranches { get; } = new();
    public ObservableCollection<string> AvailableComposeFiles { get; } = new();

    [ObservableProperty] private string _remoteRepositoryPath = string.Empty;
    [ObservableProperty] private string _branch = "main";
    [ObservableProperty] private string _composeFile = "docker-compose.yml";
    [ObservableProperty] private string? _selectedBranchSuggestion;
    [ObservableProperty] private string? _selectedComposeSuggestion;
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
        string? remoteRepositoryPath = null,
        string? composeFile = null)
    {
        _preflight = preflight;
        _deployment = deployment;
        _discovery = discovery;
        _postDeployVerification = postDeployVerification;
        _serverAccessor = serverAccessor;
        _secretAccessor = secretAccessor;
        RemoteRepositoryPath = remoteRepositoryPath?.Trim() ?? string.Empty;
        ComposeFile = composeFile?.Trim() ?? "docker-compose.yml";
    }

    partial void OnRemoteRepositoryPathChanged(string value)
    {
        AvailableBranches.Clear();
        AvailableComposeFiles.Clear();
        SelectedBranchSuggestion = null;
        SelectedComposeSuggestion = null;
        InvalidatePreflight();
    }

    partial void OnBranchChanged(string value) => InvalidatePreflight();
    partial void OnComposeFileChanged(string value) => InvalidatePreflight();
    partial void OnRequireEnvironmentFileChanged(bool value) => InvalidatePreflight();
    partial void OnEnvironmentFileNameChanged(string value) => InvalidatePreflight();

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

    public void Reset()
    {
        Checks.Clear();
        Steps.Clear();
        PostChecks.Clear();
        AvailableBranches.Clear();
        AvailableComposeFiles.Clear();
        SelectedBranchSuggestion = null;
        SelectedComposeSuggestion = null;
        CanDeploy = false;
        DeploymentOutput = string.Empty;
        LastChecked = "Never";
        StatusMessage = "Paso 1: conecta al servidor seleccionado y detecta sus ramas y archivos Compose.";
        CancelPendingDeploy();
    }

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
            StatusMessage = "Escribe la ruta del repositorio remoto y vuelve a detectar las opciones.";
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

            StatusMessage = $"Detección completada: {result.Branches.Count} rama(s) y {result.ComposeFiles.Count} archivo(s) Compose. Selecciona una sugerencia si hace falta y valida los requisitos (paso 2).";
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
}
