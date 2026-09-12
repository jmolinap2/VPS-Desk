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
    private readonly Func<ServerProfile?> _serverAccessor;
    private readonly Func<string?> _secretAccessor;

    public ObservableCollection<PreflightCheckResult> Checks { get; } = new();
    public ObservableCollection<DeploymentStepResult> Steps { get; } = new();

    [ObservableProperty] private string _remoteRepositoryPath = string.Empty;
    [ObservableProperty] private string _branch = "main";
    [ObservableProperty] private string _composeFile = "docker-compose.yml";
    [ObservableProperty] private bool _requireEnvironmentFile = true;
    [ObservableProperty] private string _environmentFileName = ".env";
    [ObservableProperty] private bool _pullImages = true;
    [ObservableProperty] private bool _buildImages = true;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _canDeploy;
    [ObservableProperty] private bool _hasPendingDeploy;
    [ObservableProperty] private string _pendingDeployMessage = string.Empty;
    [ObservableProperty] private string _statusMessage = "Run preflight before any deployment.";
    [ObservableProperty] private string _lastChecked = "Never";
    [ObservableProperty] private string _deploymentOutput = string.Empty;

    public DeploymentsViewModel(
        IDeploymentPreflightService preflight,
        IComposeDeploymentService deployment,
        Func<ServerProfile?> serverAccessor,
        Func<string?> secretAccessor)
    {
        _preflight = preflight;
        _deployment = deployment;
        _serverAccessor = serverAccessor;
        _secretAccessor = secretAccessor;
    }

    partial void OnRemoteRepositoryPathChanged(string value) => InvalidatePreflight();
    partial void OnBranchChanged(string value) => InvalidatePreflight();
    partial void OnComposeFileChanged(string value) => InvalidatePreflight();
    partial void OnRequireEnvironmentFileChanged(bool value) => InvalidatePreflight();
    partial void OnEnvironmentFileNameChanged(string value) => InvalidatePreflight();

    public void Reset()
    {
        Checks.Clear();
        Steps.Clear();
        CanDeploy = false;
        DeploymentOutput = string.Empty;
        LastChecked = "Never";
        StatusMessage = "Run preflight before any deployment.";
        CancelPendingDeploy();
    }

    [RelayCommand]
    public async Task RunPreflightAsync()
    {
        if (IsBusy) return;
        var server = _serverAccessor();
        if (server == null)
        {
            StatusMessage = "No active server. Choose one in Servers first.";
            return;
        }

        if (!ValidateConfiguration()) return;

        IsBusy = true;
        CanDeploy = false;
        CancelPendingDeploy();
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
                ? "Preflight passed. Review the target and request deployment when ready."
                : "Preflight blocked deployment. Fix failed requirements and run it again.";
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
            StatusMessage = result.Succeeded
                ? $"Deployment completed successfully in {(result.FinishedAt - result.StartedAt).TotalSeconds:F1}s. Run preflight again before another deployment."
                : $"Deployment stopped at '{result.FailedStep?.Label ?? "unknown step"}'. Review the sanitized output.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Deployment failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelDeploy() => CancelPendingDeploy();

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
