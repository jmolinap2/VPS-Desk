using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Application.Activity;
using VpsDesk.Application.Deployments;
using VpsDesk.Domain.Activity;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Desktop.ViewModels;

public partial class DeploymentsViewModel
{
    private IOperationHistoryStore? _historyStore;

    public ObservableCollection<OperationHistoryEntry> DeploymentHistory { get; } = new();

    [ObservableProperty] private OperationHistoryEntry? _selectedHistoryEntry;
    [ObservableProperty] private bool _isHistoryView;
    [ObservableProperty] private bool _isPreflightModalOpen;
    [ObservableProperty] private string _preflightModalMessage = string.Empty;
    [ObservableProperty] private bool _isPostflightModalOpen;
    [ObservableProperty] private string _postflightModalMessage = string.Empty;

    public bool IsNewDeploymentView => !IsHistoryView;
    public event EventHandler? HistoryChanged;

    partial void OnIsHistoryViewChanged(bool value)
        => OnPropertyChanged(nameof(IsNewDeploymentView));

    public void InitializeHistory(IOperationHistoryStore historyStore)
    {
        _historyStore = historyStore;
        _ = RefreshHistoryAsync();
    }

    [RelayCommand]
    private void ShowNewDeployment() => IsHistoryView = false;

    [RelayCommand]
    private async Task ShowHistoryAsync()
    {
        IsHistoryView = true;
        await RefreshHistoryAsync();
    }

    [RelayCommand]
    private void ClosePreflightModal()
    {
        if (!IsBusy) IsPreflightModalOpen = false;
    }

    [RelayCommand]
    private void ClosePostflightModal()
    {
        if (!IsBusy) IsPostflightModalOpen = false;
    }

    public async Task RefreshHistoryAsync()
    {
        if (_historyStore is null) return;
        var serverId = _serverAccessor()?.Id;
        var items = await _historyStore.GetDeploymentsAsync(serverId, 100);

        DeploymentHistory.Clear();
        foreach (var item in items) DeploymentHistory.Add(item);
        SelectedHistoryEntry ??= DeploymentHistory.FirstOrDefault();
    }

    private void BeginPreflightModal()
    {
        IsPreflightModalOpen = true;
        PreflightModalMessage = "Comprobando SSH, Git, Docker, Compose, rama, archivos y entorno...";
    }

    private async Task CompletePreflightModalAsync(bool passed)
    {
        PreflightModalMessage = passed
            ? "Prevuelo correcto. Todo está listo para continuar."
            : "El prevuelo encontró bloqueos. Revisa los elementos marcados antes de desplegar.";

        if (passed)
        {
            await Task.Delay(650);
            IsPreflightModalOpen = false;
        }
    }

    private void BeginPostflightModal()
    {
        IsPostflightModalOpen = true;
        PostflightModalMessage = "Verificando contenedores y comprobaciones HTTP después del despliegue...";
    }

    private void CompletePostflightModal(PostDeployVerificationResult verification)
    {
        PostflightModalMessage = verification.Passed
            ? verification.HasWarnings
                ? "Postvuelo completado con advertencias. Revísalas antes de dar por saludable la versión."
                : "Postvuelo correcto. La versión desplegada pasó las comprobaciones."
            : "El postvuelo encontró problemas. El despliegue terminó, pero no debe considerarse saludable todavía.";
    }

    private async Task RecordDeploymentAsync(
        ServerProfile server,
        ComposeDeploymentResult result,
        PostDeployVerificationResult? verification,
        string output)
    {
        if (_historyStore is null) return;

        var outcome = !result.Succeeded || verification?.Passed == false
            ? OperationOutcome.Failed
            : verification?.HasWarnings == true
                ? OperationOutcome.Warning
                : OperationOutcome.Success;

        var details = new StringBuilder();
        if (verification is not null)
        {
            foreach (var check in verification.Checks)
            {
                details.AppendLine($"[{check.Status}] {check.Label}: {check.Detail}");
            }
        }

        var summary = outcome switch
        {
            OperationOutcome.Success => $"Despliegue correcto · {Branch.Trim()} · {ShortCommit(result.DeployedCommit)}",
            OperationOutcome.Warning => $"Despliegue con advertencias · {Branch.Trim()} · {ShortCommit(result.DeployedCommit)}",
            _ => $"Despliegue fallido · {Branch.Trim()} · {result.FailedStep?.Label ?? "verificación post-despliegue"}"
        };

        var entry = new OperationHistoryEntry(
            Guid.NewGuid(),
            server.Id,
            server.Name,
            server.Environment.ToString(),
            OperationKind.Deployment,
            "Deploy",
            summary,
            outcome,
            result.StartedAt,
            result.FinishedAt,
            RemoteRepositoryPath.Trim(),
            Branch.Trim(),
            result.PreviousCommit,
            result.DeployedCommit,
            ComposeFile.Trim(),
            result.FailedStep?.Label,
            output,
            details.Length == 0 ? null : details.ToString().TrimEnd());

        await _historyStore.AddAsync(entry);
        await RefreshHistoryAsync();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string ShortCommit(string? commit)
        => string.IsNullOrWhiteSpace(commit)
            ? "sin SHA"
            : commit.Length <= 8 ? commit : commit[..8];
}
