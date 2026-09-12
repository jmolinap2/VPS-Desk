using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Application.Abstractions;
using VpsDesk.Application.Activity;
using VpsDesk.Domain.Activity;
using VpsDesk.Domain.Servers;
using VpsDesk.Domain.Storage;

namespace VpsDesk.Desktop.ViewModels;

public partial class StorageViewModel : ObservableObject
{
    private readonly IStorageService _storage;
    private readonly Func<ServerProfile?> _serverAccessor;
    private readonly Func<string?> _secretAccessor;
    private readonly IOperationHistoryStore? _historyStore;
    private DateTimeOffset? _lastRefreshUtc;
    private StorageCleanupKind? _pendingCleanupKind;

    public ObservableCollection<DockerDiskUsage> DockerUsage { get; } = new();
    public ObservableCollection<DockerImageInfo> Images { get; } = new();
    public ObservableCollection<DockerVolumeInfo> Volumes { get; } = new();
    public ObservableCollection<HeavyDirectoryInfo> HeavyDirectories { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _rootUsagePercent;
    [ObservableProperty] private string _rootUsed = "--";
    [ObservableProperty] private string _rootTotal = "--";
    [ObservableProperty] private string _rootAvailable = "--";
    [ObservableProperty] private string _statusMessage = "Open Storage to inspect disk and Docker usage.";
    [ObservableProperty] private string _lastUpdated = "Never";
    [ObservableProperty] private string _imagesReclaimable = "--";
    [ObservableProperty] private string _buildCacheReclaimable = "--";
    [ObservableProperty] private string _volumesReclaimable = "--";
    [ObservableProperty] private bool _hasPendingCleanup;
    [ObservableProperty] private bool _includeUnusedVolumes;
    [ObservableProperty] private bool _showIncludeVolumesOption;
    [ObservableProperty] private string _pendingCleanupTitle = string.Empty;
    [ObservableProperty] private string _pendingCleanupMessage = string.Empty;
    [ObservableProperty] private string _cleanupOutput = string.Empty;

    public event EventHandler? HistoryChanged;

    public int ImageCount => Images.Count;
    public int VolumeCount => Volumes.Count;
    public bool HasDockerStorage => DockerUsage.Count > 0 || Images.Count > 0 || Volumes.Count > 0;

    public StorageViewModel(
        IStorageService storage,
        Func<ServerProfile?> serverAccessor,
        Func<string?> secretAccessor,
        IOperationHistoryStore? historyStore = null)
    {
        _storage = storage;
        _serverAccessor = serverAccessor;
        _secretAccessor = secretAccessor;
        _historyStore = historyStore;
    }

    public async Task RefreshIfNeededAsync()
    {
        if (_lastRefreshUtc == null || DateTimeOffset.UtcNow - _lastRefreshUtc > TimeSpan.FromSeconds(20))
        {
            await RefreshAsync();
        }
    }

    public void Reset()
    {
        RootUsagePercent = 0;
        RootUsed = "--";
        RootTotal = "--";
        RootAvailable = "--";
        ImagesReclaimable = "--";
        BuildCacheReclaimable = "--";
        VolumesReclaimable = "--";
        DockerUsage.Clear();
        Images.Clear();
        Volumes.Clear();
        HeavyDirectories.Clear();
        LastUpdated = "Never";
        StatusMessage = "Select an active server, then refresh storage.";
        CleanupOutput = string.Empty;
        _lastRefreshUtc = null;
        CancelCleanup();
        NotifyCollectionSummaries();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        var server = _serverAccessor();
        if (server == null)
        {
            Reset();
            StatusMessage = "No active server. Choose one in Servers first.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Reading filesystem, Docker storage and large directories...";

        try
        {
            var snapshot = await _storage.ReadAsync(server, _secretAccessor());
            RootUsagePercent = Math.Clamp(snapshot.RootUsagePercent, 0, 100);
            RootUsed = FormatBytes(snapshot.RootUsedBytes);
            RootTotal = FormatBytes(snapshot.RootTotalBytes);
            RootAvailable = FormatBytes(snapshot.RootAvailableBytes);

            Replace(DockerUsage, snapshot.DockerUsage);
            Replace(Images, snapshot.Images);
            Replace(Volumes, snapshot.Volumes);
            Replace(HeavyDirectories, snapshot.HeavyDirectories);

            ImagesReclaimable = FindReclaimable("Images");
            BuildCacheReclaimable = FindReclaimable("Build Cache");
            VolumesReclaimable = FindReclaimable("Local Volumes");

            _lastRefreshUtc = DateTimeOffset.UtcNow;
            LastUpdated = DateTimeOffset.Now.ToString("HH:mm:ss");
            StatusMessage = HasDockerStorage
                ? $"Storage updated · {ImageCount} images · {VolumeCount} volumes · cleanup is guarded by confirmation."
                : "Filesystem updated. Docker storage details are unavailable or Docker is not installed.";
            NotifyCollectionSummaries();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unable to read storage: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void RequestBuildCacheCleanup() => RequestCleanup(StorageCleanupKind.BuildCache);

    [RelayCommand]
    private void RequestUnusedImagesCleanup() => RequestCleanup(StorageCleanupKind.UnusedImages);

    [RelayCommand]
    private void RequestDockerSystemCleanup() => RequestCleanup(StorageCleanupKind.DockerSystem);

    [RelayCommand]
    private async Task ConfirmCleanupAsync()
    {
        if (!HasPendingCleanup || _pendingCleanupKind is null || IsBusy) return;
        var server = _serverAccessor();
        if (server == null)
        {
            StatusMessage = "The active server changed. Cleanup cancelled.";
            CancelCleanup();
            return;
        }

        var kind = _pendingCleanupKind.Value;
        var includeVolumes = kind == StorageCleanupKind.DockerSystem && IncludeUnusedVolumes;
        var startedAt = DateTimeOffset.UtcNow;
        IsBusy = true;
        HasPendingCleanup = false;
        CleanupOutput = string.Empty;
        StatusMessage = $"Running {GetActionLabel(kind).ToLowerInvariant()} on {server.Name}...";

        try
        {
            var result = await _storage.CleanupAsync(
                server,
                new StorageCleanupRequest(kind, includeVolumes),
                _secretAccessor());

            CleanupOutput = result.Output;
            StatusMessage = result.Succeeded
                ? $"{GetActionLabel(kind)} completed in {result.Duration.TotalSeconds:F1}s. Storage will be refreshed."
                : $"{GetActionLabel(kind)} failed with exit code {result.ExitCode}. Review the output.";

            await RecordCleanupAsync(server, startedAt, result);
        }
        catch (Exception ex)
        {
            CleanupOutput = ex.Message;
            StatusMessage = $"Cleanup failed: {ex.Message}";
            await RecordCleanupExceptionAsync(server, startedAt, kind, ex);
        }
        finally
        {
            IsBusy = false;
            _pendingCleanupKind = null;
            IncludeUnusedVolumes = false;
            ShowIncludeVolumesOption = false;
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private void CancelCleanup()
    {
        _pendingCleanupKind = null;
        HasPendingCleanup = false;
        IncludeUnusedVolumes = false;
        ShowIncludeVolumesOption = false;
        PendingCleanupTitle = string.Empty;
        PendingCleanupMessage = string.Empty;
    }

    private void RequestCleanup(StorageCleanupKind kind)
    {
        var server = _serverAccessor();
        if (server == null)
        {
            StatusMessage = "No active server. Choose one in Servers first.";
            return;
        }

        _pendingCleanupKind = kind;
        ShowIncludeVolumesOption = kind == StorageCleanupKind.DockerSystem;
        IncludeUnusedVolumes = false;
        HasPendingCleanup = true;
        PendingCleanupTitle = GetActionLabel(kind);

        var productionWarning = server.Environment == ServerEnvironment.Production
            ? " Este servidor está marcado como PRODUCCIÓN."
            : string.Empty;
        var actionWarning = kind switch
        {
            StorageCleanupKind.BuildCache => "Se eliminará únicamente caché de build de Docker que pueda reconstruirse.",
            StorageCleanupKind.UnusedImages => "Se eliminarán imágenes Docker no usadas por ningún contenedor.",
            _ => "Se eliminarán contenedores detenidos, redes sin uso, imágenes sin uso y caché de build. Los volúmenes NO se eliminan salvo que marques la opción explícitamente."
        };
        PendingCleanupMessage = actionWarning + productionWarning;
    }

    private async Task RecordCleanupAsync(ServerProfile server, DateTimeOffset startedAt, StorageCleanupResult result)
    {
        if (_historyStore is null) return;
        var action = GetActionLabel(result.Kind);
        await _historyStore.AddAsync(new OperationHistoryEntry(
            Guid.NewGuid(),
            server.Id,
            server.Name,
            server.Environment.ToString(),
            OperationKind.StorageCleanup,
            action,
            result.Succeeded ? $"{action} completada" : $"{action} fallida",
            result.Succeeded ? OperationOutcome.Success : OperationOutcome.Failed,
            startedAt,
            result.FinishedAtUtc,
            Output: result.Output,
            Details: result.IncludeUnusedVolumes ? "Incluyó volúmenes Docker sin uso." : "No eliminó volúmenes Docker."));
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task RecordCleanupExceptionAsync(
        ServerProfile server,
        DateTimeOffset startedAt,
        StorageCleanupKind kind,
        Exception exception)
    {
        if (_historyStore is null) return;
        var action = GetActionLabel(kind);
        await _historyStore.AddAsync(new OperationHistoryEntry(
            Guid.NewGuid(),
            server.Id,
            server.Name,
            server.Environment.ToString(),
            OperationKind.StorageCleanup,
            action,
            $"{action} fallida",
            OperationOutcome.Failed,
            startedAt,
            DateTimeOffset.UtcNow,
            FailedStep: action,
            Output: exception.Message,
            Details: exception.ToString()));
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private string FindReclaimable(string type)
        => DockerUsage.FirstOrDefault(x => x.Type.Equals(type, StringComparison.OrdinalIgnoreCase))?.Reclaimable ?? "--";

    private static string GetActionLabel(StorageCleanupKind kind) => kind switch
    {
        StorageCleanupKind.BuildCache => "Limpiar caché de build",
        StorageCleanupKind.UnusedImages => "Limpiar imágenes no usadas",
        StorageCleanupKind.DockerSystem => "Limpieza Docker completa",
        _ => "Limpieza de almacenamiento"
    };

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source) target.Add(item);
    }

    private void NotifyCollectionSummaries()
    {
        OnPropertyChanged(nameof(ImageCount));
        OnPropertyChanged(nameof(VolumeCount));
        OnPropertyChanged(nameof(HasDockerStorage));
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:F1} {units[index]}";
    }
}
