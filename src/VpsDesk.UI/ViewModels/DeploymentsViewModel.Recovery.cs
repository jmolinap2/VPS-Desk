using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Application.Deployments;
using VpsDesk.Application.Snapshots;
using VpsDesk.Desktop.Services;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Desktop.ViewModels;

public partial class DeploymentsViewModel
{
    [ObservableProperty] private bool _isRecoveryChecking;
    [ObservableProperty] private bool _requiresRecoveryReview;

    partial void OnHasDeploymentRecoveryChanged(bool value)
    {
        OnPropertyChanged(nameof(HasExecutionActivity));
        OnPropertyChanged(nameof(CanRestartDeployment));
    }
    [ObservableProperty] private bool _recoveryReviewConfirmed;

    private bool TryReadRecovery(ServerProfile server, out DeploymentRecoveryRecord? recovery)
    {
        try
        {
            recovery = _profileStore.ReadRecovery(server.Id);
            return true;
        }
        catch (Exception exception)
        {
            recovery = null;
            Toast.Error(exception, "No se pudo leer el estado de recuperación");
            return false;
        }
    }

    private void ShowSavedRecovery(ServerProfile server, DeploymentRecoveryRecord? recovery)
    {
        if (recovery is null) return;
        _deploymentSnapshot = recovery.Snapshot;
        _deploymentSnapshotServerId = server.Id;
        HasDeploymentRecovery = true;
        SuggestDeploymentRestore = recovery.Snapshot is { } snapshot
            && (snapshot.ExpiresAt is null || snapshot.ExpiresAt > DateTimeOffset.UtcNow);
        DeploymentRecoveryMessage = $"Recuperación pendiente para {recovery.ProjectPath}. {recovery.Status}. "
            + "El snapshot no se reemplazará automáticamente.";
        DeploymentNextAction = DeploymentRecoveryMessage;
        OnPropertyChanged(nameof(CanRestoreDeploymentSnapshot));
    }

    private string? _previousDeploymentCommit;
    private string? _deployedCommit;
    private VpsSnapshotInfo? _deploymentSnapshot;
    private Guid? _deploymentSnapshotServerId;
    [ObservableProperty] private bool _hasDeploymentRecovery;
    [ObservableProperty] private bool _suggestDeploymentRestore;
    [ObservableProperty] private string _deploymentRecoveryMessage = string.Empty;

    public bool CanRestoreDeploymentSnapshot => SuggestDeploymentRestore && CanUseSnapshots
        && _deploymentSnapshotServerId == _serverAccessor()?.Id && _deploymentSnapshot is not null;

    private void ClearDeploymentRecovery()
    {
        HasDeploymentRecovery = false;
        SuggestDeploymentRestore = false;
        DeploymentRecoveryMessage = string.Empty;
        _deploymentSnapshot = null;
        _deploymentSnapshotServerId = null;
        OnPropertyChanged(nameof(CanRestoreDeploymentSnapshot));
    }

    private async Task<PostDeployVerificationResult?> CheckFailedDeploymentAsync(ServerProfile server, PostDeployVerificationRequest verificationRequest, string? secret)
    {
        StatusMessage = "Recuperando conexión SSH y comprobando el estado del VPS...";
        DeploymentNextAction = "Comprobando los servicios después del fallo. Espera el diagnóstico antes de repetir el pase.";
        try
        {
            // VerifyAsync abre conexiones nuevas y reintenta las consultas de lectura.
            var verification = await _postDeployVerification.VerifyAsync(verificationRequest, secret);
            if (_serverAccessor()?.Id != server.Id) return null;
            PostChecks.Clear();
            foreach (var check in verification.Checks) PostChecks.Add(check);
            ApplyDeploymentRecovery(verification);
            return verification;
        }
        catch (Exception ex)
        {
            if (_serverAccessor()?.Id == server.Id) ShowUnconfirmedRecovery(UserFacingErrors.Describe(ex));
            return null;
        }
    }

    private void ShowUnconfirmedRecovery(string detail)
    {
        HasDeploymentRecovery = true;
        SuggestDeploymentRestore = _deploymentSnapshot is not null;
        OnPropertyChanged(nameof(CanRestoreDeploymentSnapshot));
        DeploymentRecoveryMessage = "Estado sin confirmar. No se pudo recuperar la conexión o completar las comprobaciones. "
            + "Revisa el VPS en el panel del proveedor; perder SSH no confirma que el servidor esté roto. " + detail;
        DeploymentOutcomeTitle = "Pase interrumpido · estado sin confirmar";
        DeploymentNextAction = DeploymentRecoveryMessage;
        StatusMessage = "No se pudo confirmar el estado del VPS después del pase.";
    }

    private void ApplyDeploymentRecovery(PostDeployVerificationResult verification)
    {
        if (!verification.IsStateConfirmed && !verification.HasConfirmedFailure)
        {
            ShowUnconfirmedRecovery(string.Join(" · ", verification.Checks.Where(c => c.StateUnconfirmed).Select(c => c.Detail)));
            return;
        }

        HasDeploymentRecovery = true;
        var failures = verification.Checks.Where(c => c.Status == PostDeployCheckStatus.Failed && !c.StateUnconfirmed).ToArray();
        SuggestDeploymentRestore = failures.Length > 0 && _deploymentSnapshot is { } snapshot
            && (snapshot.ExpiresAt is null || snapshot.ExpiresAt > DateTimeOffset.UtcNow)
            && _deploymentSnapshotServerId == _serverAccessor()?.Id;
        DeploymentRecoveryMessage = failures.Length == 0
            ? "Se recuperó la conexión. Revisa las comprobaciones, el paso fallido y las migraciones antes de repetir el pase."
            : "Se recuperó la conexión y se confirmaron problemas: "
                + string.Join(" · ", failures.Select(c => $"{c.Label}: {c.Detail}"))
                + (SuggestDeploymentRestore
                    ? $" Puedes restaurar el snapshot previo a este pase, creado el {_deploymentSnapshot!.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm}. Restaurar revierte TODO el VPS y pierde los datos escritos después."
                    : " No hay un snapshot vigente vinculado a este pase. Revisa los servicios y las opciones de recuperación del proveedor.");
        DeploymentNextAction = DeploymentRecoveryMessage;
        StatusMessage = failures.Length == 0 ? "Comprobación tras el fallo: servicios disponibles." : "Comprobación tras el fallo: problemas confirmados.";
        OnPropertyChanged(nameof(CanRestoreDeploymentSnapshot));
    }

    [RelayCommand]
    private async Task VerifyDeploymentRecoveryAsync()
    {
        if (IsBusy || IsSnapshotBusy || IsAppBackupBusy || _serverAccessor() is not { } server
            || !TryReadRecovery(server, out var recovery) || recovery is null) return;

        var sameProject = RemoteRepositoryPath.Trim().TrimEnd('/') == recovery.ProjectPath
            && ComposeFile.Trim() == recovery.ComposeFile;
        var urls = (recovery.HealthUrls ?? []).Concat(sameProject ? ParseHealthUrls() : [])
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var request = new PostDeployVerificationRequest(server, recovery.ProjectPath, recovery.ComposeFile, urls, recovery.Services);
        var secret = _secretAccessor();
        IsSnapshotBusy = true;
        IsRecoveryChecking = true;
        try
        {
            var verification = await CheckFailedDeploymentAsync(server, request, secret);
            if (_serverAccessor()?.Id != server.Id) return;
            if (!recovery.NeedsReview && verification?.IsHealthy == true)
            {
                _profileStore.ClearRecovery(server.Id);
                ClearDeploymentRecovery();
                DeploymentNextAction = "La salud de los servicios comprobados está confirmada. Se cerró la recuperación pendiente.";
                StatusMessage = DeploymentNextAction;
                Toast.Success(DeploymentNextAction, "Recuperación completada");
            }
            else
            {
                if (recovery.NeedsReview)
                    DeploymentNextAction += " El pase anterior todavía requiere revisión de sus comandos o migraciones.";
                Toast.Warning(DeploymentNextAction, "Recuperación pendiente");
            }
        }
        catch (Exception exception)
        {
            Toast.Error(exception, "No se pudo completar la revisión de recuperación");
        }
        finally { IsRecoveryChecking = false; IsSnapshotBusy = false; }
    }

    [RelayCommand]
    private async Task RequestRestoreDeploymentSnapshotAsync()
    {
        if (!CanRestoreDeploymentSnapshot || _deploymentSnapshot is not { } expected
            || _serverAccessor() is not { } server || _snapshots is null) return;
        IsSnapshotBusy = true;
        try
        {
            var current = await _snapshots.GetAsync(server.Host);
            if (_serverAccessor()?.Id != server.Id) return;
            if (!SnapshotRecoveryPolicy.IsSameAvailableSnapshot(expected, current, DateTimeOffset.UtcNow))
            {
                SuggestDeploymentRestore = false;
                DeploymentRecoveryMessage = "El snapshot previo a este pase ya no está disponible o fue reemplazado. Revisa el panel del proveedor.";
                Toast.Warning(DeploymentRecoveryMessage, "Snapshot no disponible");
                return;
            }
            ApplySnapshot(current);
        }
        catch (Exception ex)
        {
            Toast.Error(ex, "No se pudo comprobar el snapshot del pase");
            return;
        }
        finally { IsSnapshotBusy = false; }
        // Abre la confirmación existente con frase; nunca restaura automáticamente.
        RequestRestoreSnapshot();
    }
}
