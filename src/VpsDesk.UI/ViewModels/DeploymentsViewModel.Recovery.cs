using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Application.Deployments;
using VpsDesk.Application.Snapshots;
using VpsDesk.Desktop.Services;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Desktop.ViewModels;

public partial class DeploymentsViewModel
{
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

    private async Task CheckFailedDeploymentAsync(ServerProfile server, PostDeployVerificationRequest verificationRequest)
    {
        StatusMessage = "Recuperando conexión SSH y comprobando el estado del VPS...";
        DeploymentNextAction = "Comprobando los servicios después del fallo. Espera el diagnóstico antes de repetir el pase.";
        try
        {
            // VerifyAsync abre conexiones nuevas y reintenta las consultas de lectura.
            var verification = await _postDeployVerification.VerifyAsync(verificationRequest, _secretAccessor());
            if (_serverAccessor()?.Id != server.Id) return;
            PostChecks.Clear();
            foreach (var check in verification.Checks) PostChecks.Add(check);
            ApplyDeploymentRecovery(verification);
        }
        catch (Exception ex)
        {
            if (_serverAccessor()?.Id != server.Id) return;
            ShowUnconfirmedRecovery(UserFacingErrors.Describe(ex));
        }
    }

    private void ShowUnconfirmedRecovery(string detail)
    {
        HasDeploymentRecovery = true;
        SuggestDeploymentRestore = false;
        DeploymentRecoveryMessage = "Estado sin confirmar. No se pudo recuperar la conexión o completar las comprobaciones. "
            + "Revisa el VPS en el panel del proveedor; perder SSH no confirma que el servidor esté roto. " + detail;
        DeploymentOutcomeTitle = "Pase interrumpido · estado sin confirmar";
        DeploymentNextAction = DeploymentRecoveryMessage;
        StatusMessage = "No se pudo confirmar el estado del VPS después del pase.";
    }

    private void ApplyDeploymentRecovery(PostDeployVerificationResult verification)
    {
        if (!verification.IsStateConfirmed)
        {
            ShowUnconfirmedRecovery(string.Join(" · ", verification.Checks.Where(c => c.StateUnconfirmed).Select(c => c.Detail)));
            return;
        }

        HasDeploymentRecovery = true;
        var failures = verification.Checks.Where(c => c.Status == PostDeployCheckStatus.Failed).ToArray();
        SuggestDeploymentRestore = failures.Length > 0 && _deploymentSnapshot is { } snapshot
            && (snapshot.ExpiresAt is null || snapshot.ExpiresAt > DateTimeOffset.UtcNow)
            && _deploymentSnapshotServerId == _serverAccessor()?.Id;
        DeploymentRecoveryMessage = failures.Length == 0
            ? "Conexión recuperada. Los servicios comprobados responden. Revisa el paso fallido y las migraciones antes de repetir el pase."
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
