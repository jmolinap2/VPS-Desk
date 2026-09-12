using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Application.Abstractions;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IServerProbeService _probe;
    private readonly ServerProfile? _server;
    private readonly string? _secret;

    [ObservableProperty]
    private string _selectedServerName = "No server selected";

    [ObservableProperty]
    private string _connectionStatus = "Offline";

    [ObservableProperty]
    private string _compatibilityLabel = "Compatibility unknown";

    [ObservableProperty]
    private string _dockerStatus = "Unknown";

    [ObservableProperty]
    private string _nginxStatus = "Unknown";

    [ObservableProperty]
    private string _lastChecked = "Never";

    [ObservableProperty]
    private string _loadSummary = "Load -- / -- / --";

    [ObservableProperty]
    private string _memorySummary = "Awaiting connection";

    [ObservableProperty]
    private string _networkSummary = "Network RX/TX --";

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private double _cpuUsage;

    [ObservableProperty]
    private double _memoryUsage;

    [ObservableProperty]
    private double _diskUsage;

    [ObservableProperty]
    private string _uptime = "--";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isRefreshing;

    public ObservableCollection<double> CpuSeries { get; } = new();
    public ObservableCollection<double> MemorySeries { get; } = new();

    public MainWindowViewModel(IServerProbeService probe, ServerProfile? server, string? secret, string? warning = null)
    {
        _probe = probe;
        _server = server;
        _secret = secret;

        if (_server != null)
        {
            SelectedServerName = _server.Name;
            CompatibilityLabel = string.Equals(_server.ProviderLabel, "Hostinger", StringComparison.OrdinalIgnoreCase)
                ? "HOSTINGER · PENDING CHECK"
                : "GENERIC LINUX · PENDING CHECK";
        }

        if (!string.IsNullOrWhiteSpace(warning))
        {
            StatusMessage = warning;
        }

        for (var i = 0; i < 30; i++)
        {
            CpuSeries.Add(0);
            MemorySeries.Add(0);
        }
    }

    private bool CanRefresh() => !IsRefreshing && _server != null;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public async Task RefreshAsync()
    {
        if (_server == null) return;

        IsRefreshing = true;
        StatusMessage = "Checking SSH and server capabilities...";

        try
        {
            var compatibility = await _probe.CheckCompatibilityAsync(_server, _secret);
            var ssh = compatibility.Capabilities.FirstOrDefault(x => x.Capability == ServerCapability.Ssh);
            ConnectionStatus = ssh?.Available == true ? "SSH Online" : "SSH Offline";
            CompatibilityLabel = compatibility.Level switch
            {
                CompatibilityLevel.Certified => $"{_server.ProviderLabel?.ToUpperInvariant()} · CERTIFIED",
                CompatibilityLevel.Compatible => "GENERIC LINUX · COMPATIBLE",
                CompatibilityLevel.Partial => "PARTIAL COMPATIBILITY",
                CompatibilityLevel.Unsupported => "UNSUPPORTED",
                _ => "COMPATIBILITY UNKNOWN"
            };

            DockerStatus = CapabilityText(compatibility, ServerCapability.Docker);
            NginxStatus = CapabilityText(compatibility, ServerCapability.Nginx);

            if (ssh?.Available != true)
            {
                StatusMessage = ssh?.Detail ?? "SSH connection failed.";
                LastChecked = DateTimeOffset.Now.ToString("HH:mm:ss");
                return;
            }

            StatusMessage = "Reading live telemetry...";
            var metrics = await _probe.ReadMetricsAsync(_server, _secret);

            CpuUsage = Math.Clamp(metrics.CpuUsagePercent, 0, 100);
            MemoryUsage = Math.Clamp(metrics.MemoryUsagePercent, 0, 100);
            DiskUsage = Math.Clamp(metrics.DiskUsagePercent, 0, 100);
            Uptime = FormatUptime(metrics.Uptime);
            LoadSummary = $"Load {metrics.Load1:F2} / {metrics.Load5:F2} / {metrics.Load15:F2}";
            MemorySummary = $"{FormatBytes(metrics.MemoryUsedBytes)} / {FormatBytes(metrics.MemoryTotalBytes)}";
            NetworkSummary = $"RX {FormatRate(metrics.NetworkRxBytesPerSecond)} · TX {FormatRate(metrics.NetworkTxBytesPerSecond)}";

            Push(CpuSeries, CpuUsage, 60);
            Push(MemorySeries, MemoryUsage, 60);

            LastChecked = DateTimeOffset.Now.ToString("HH:mm:ss");
            StatusMessage = compatibility.Distribution is { Length: > 0 }
                ? $"{compatibility.Distribution} · {compatibility.Kernel}"
                : "Telemetry updated.";
        }
        catch (Exception ex)
        {
            ConnectionStatus = "SSH Offline";
            StatusMessage = ex.Message;
            LastChecked = DateTimeOffset.Now.ToString("HH:mm:ss");
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private static string CapabilityText(CompatibilitySnapshot snapshot, ServerCapability capability)
    {
        var result = snapshot.Capabilities.FirstOrDefault(x => x.Capability == capability);
        if (result == null || !result.Available) return "Unavailable";
        return string.IsNullOrWhiteSpace(result.Version) ? "Available" : result.Version;
    }

    private static void Push(ObservableCollection<double> series, double value, int max)
    {
        series.Add(value);
        while (series.Count > max) series.RemoveAt(0);
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1) return $"{(int)uptime.TotalDays}d {uptime.Hours}h";
        if (uptime.TotalHours >= 1) return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
        return $"{Math.Max(0, uptime.Minutes)}m";
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

    private static string FormatRate(double bytesPerSecond) => $"{FormatBytes((long)Math.Max(0, bytesPerSecond))}/s";
}
