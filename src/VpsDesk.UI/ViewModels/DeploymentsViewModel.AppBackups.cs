using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Application.AppBackups;
using VpsDesk.Desktop.Services;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Desktop.ViewModels;

/// <summary>
/// Copias de la aplicación descargadas a esta máquina (PostgreSQL + proyecto +
/// configuración). Complementan al snapshot del proveedor, que es uno solo, caduca
/// al día y no se descarga: aquí se conservan las últimas N, también para
/// recuperar un problema que se detecta días después de un pase.
/// </summary>
public partial class DeploymentsViewModel
{
    private const int DefaultKeepCopies = 3;
    private const int MaxKeepCopiesUi = 10;

    private IAppBackupService? _appBackups;

    [ObservableProperty] private bool _backupBeforeDeploy;
    [ObservableProperty] private bool _isAppBackupBusy;
    [ObservableProperty] private decimal? _appBackupKeepCopies = DefaultKeepCopies;
    [ObservableProperty] private string _appBackupSummary = "Consultando las copias locales...";

    public bool IsAppBackupConfigured => _appBackups is not null;
    public bool CanUseAppBackups => IsAppBackupConfigured && !IsBusy && !IsSnapshotBusy && !IsAppBackupBusy;

    private int KeepCopies => (int)Math.Clamp(AppBackupKeepCopies ?? DefaultKeepCopies, 1, MaxKeepCopiesUi);

    partial void OnIsAppBackupBusyChanged(bool value) => OnPropertyChanged(nameof(CanUseAppBackups));

    public void InitializeAppBackups(IAppBackupService service)
    {
        _appBackups = service;
        OnPropertyChanged(nameof(IsAppBackupConfigured));
        OnPropertyChanged(nameof(CanUseAppBackups));
        RefreshAppBackups();
    }

    [RelayCommand]
    private void RefreshAppBackups()
    {
        if (_appBackups is null) return;

        if (_serverAccessor() is not { } server)
        {
            AppBackupSummary = "Selecciona un servidor para ver sus copias.";
            return;
        }

        try
        {
            var backups = _appBackups.List(server);
            AppBackupSummary = backups.Count == 0
                ? $"Sin copias locales todavía. Se guardarán en {_appBackups.GetBackupDirectory(server)}."
                : $"{backups.Count} copia(s) local(es) · última {backups[0].CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm} ({FormatBytes(backups[0].SizeBytes)}) · {_appBackups.GetBackupDirectory(server)}";
        }
        catch (Exception ex)
        {
            AppBackupSummary = $"No se pudieron leer las copias locales: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenAppBackupFolder()
    {
        if (_appBackups is null || _serverAccessor() is not { } server) return;

        try
        {
            var directory = _appBackups.GetBackupDirectory(server);
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusMessage = $"No se pudo abrir la carpeta de copias: {ex.Message}";
            Toast.Error(ex, "No se pudo abrir la carpeta de copias");
        }
    }

    [RelayCommand]
    private async Task CreateAppBackupAsync()
    {
        if (!CanUseAppBackups) return;

        if (_serverAccessor() is not { } server)
        {
            StatusMessage = "Selecciona y conecta un servidor antes de crear una copia.";
            Toast.Warning(StatusMessage, "Falta un servidor");
            return;
        }

        await CreateAppBackupWithFeedbackAsync(
            server,
            "Creando copia de la aplicación",
            $"Se guardará en esta máquina y se conservarán las {KeepCopies} más recientes. El servidor sigue funcionando.",
            "Copia creada y verificada.",
            closeOnSuccess: false);
    }

    /// <summary>Línea para la confirmación del despliegue.</summary>
    private string BuildAppBackupNotice()
        => BackupBeforeDeploy && IsAppBackupConfigured
            ? $" Antes de desplegar se descargará una copia de la aplicación (se conservan las {KeepCopies} más recientes)."
            : string.Empty;

    /// <summary>
    /// Paso previo al despliegue. Devuelve false si la copia era obligatoria y no
    /// se pudo hacer: sin ella no se toca el servidor.
    /// </summary>
    private async Task<bool> TakeAppBackupBeforeDeployAsync(ServerProfile server)
    {
        if (!BackupBeforeDeploy || _appBackups is null) return true;

        CancelPendingDeploy();
        var created = await CreateAppBackupWithFeedbackAsync(
            server,
            "Creando copia previa al despliegue",
            "Volcado de PostgreSQL, proyecto y configuración descargados a esta máquina antes de tocar el servidor.",
            "Copia creada. Continuando con el despliegue...",
            closeOnSuccess: true);

        if (!created)
        {
            StatusMessage = "Despliegue cancelado: no se pudo crear la copia previa. No se modificó el servidor.";
            Toast.Error(StatusMessage, "Despliegue cancelado");
        }

        return created;
    }

    private async Task<bool> CreateAppBackupWithFeedbackAsync(
        ServerProfile server,
        string title,
        string startMessage,
        string successMessage,
        bool closeOnSuccess)
    {
        if (string.IsNullOrWhiteSpace(RemoteRepositoryPath))
        {
            StatusMessage = "Indica o detecta primero la ruta remota del proyecto: es lo que se incluye en la copia.";
            Toast.Warning(StatusMessage, "Falta la ruta del proyecto");
            return false;
        }

        IsAppBackupBusy = true;
        BeginOperationFeedback(title, startMessage);
        var progress = new Progress<string>(AppendSnapshotLog);

        try
        {
            var result = await _appBackups!.CreateAsync(
                new AppBackupRequest(server, RemoteRepositoryPath.Trim(), KeepCopies),
                _secretAccessor(),
                progress);

            foreach (var warning in result.Warnings) AppendSnapshotLog($"AVISO: {warning}");
            AppendSnapshotLog($"Guardada en {result.Backup.FullPath} ({FormatBytes(result.Backup.SizeBytes)})");
            RefreshAppBackups();

            var detail = result.DatabaseIncluded ? string.Empty : " ATENCIÓN: no incluye base de datos.";
            var pruned = result.PrunedCount > 0 ? $" Se eliminaron {result.PrunedCount} copia(s) antigua(s)." : string.Empty;
            CompleteOperationFeedback(true, successMessage + pruned + detail);
            if (closeOnSuccess && result.DatabaseIncluded && result.Warnings.Count == 0) IsOperationFeedbackModalOpen = false;
            return true;
        }
        catch (Exception ex)
        {
            AppendSnapshotLog($"ERROR: {ex.Message}");
            CompleteOperationFeedback(false, $"No se pudo crear la copia: {UserFacingErrors.Describe(ex)}");
            return false;
        }
        finally
        {
            IsAppBackupBusy = false;
        }
    }

    private static string FormatBytes(long bytes)
        => bytes switch
        {
            < 1024 * 1024 => $"{Math.Max(1, bytes / 1024)} KB",
            < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:F1} MB",
            _ => $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB"
        };
}
