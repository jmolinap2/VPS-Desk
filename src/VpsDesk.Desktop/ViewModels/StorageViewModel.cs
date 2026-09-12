using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Application.Abstractions;
using VpsDesk.Domain.Servers;
using VpsDesk.Domain.Storage;

namespace VpsDesk.Desktop.ViewModels;

public partial class StorageViewModel : ObservableObject
{
    private readonly IStorageService _storage;
    private readonly Func<ServerProfile?> _serverAccessor;
    private readonly Func<string?> _secretAccessor;
    private DateTimeOffset? _lastRefreshUtc;

    public ObservableCollection<DockerDiskUsage> DockerUsage { get; } = new();
    public ObservableCollection<DockerImageInfo> Images { get; } = new();
    public ObservableCollection<DockerVolumeInfo> Volumes { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _rootUsagePercent;
    [ObservableProperty] private string _rootUsed = "--";
    [ObservableProperty] private string _rootTotal = "--";
    [ObservableProperty] private string _rootAvailable = "--";
    [ObservableProperty] private string _statusMessage = "Open Storage to inspect disk and Docker usage.";
    [ObservableProperty] private string _lastUpdated = "Never";

    public int ImageCount => Images.Count;
    public int VolumeCount => Volumes.Count;
    public bool HasDockerStorage => DockerUsage.Count > 0 || Images.Count > 0 || Volumes.Count > 0;

    public StorageViewModel(
        IStorageService storage,
        Func<ServerProfile?> serverAccessor,
        Func<string?> secretAccessor)
    {
        _storage = storage;
        _serverAccessor = serverAccessor;
        _secretAccessor = secretAccessor;
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
        DockerUsage.Clear();
        Images.Clear();
        Volumes.Clear();
        LastUpdated = "Never";
        StatusMessage = "Select an active server, then refresh storage.";
        _lastRefreshUtc = null;
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
        StatusMessage = "Reading filesystem and Docker storage usage...";

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

            _lastRefreshUtc = DateTimeOffset.UtcNow;
            LastUpdated = DateTimeOffset.Now.ToString("HH:mm:ss");
            StatusMessage = HasDockerStorage
                ? $"Storage updated · {ImageCount} images · {VolumeCount} volumes."
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
