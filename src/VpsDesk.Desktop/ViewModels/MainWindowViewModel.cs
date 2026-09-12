using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView.Extensions;
using VpsDesk.Application.Abstractions;
using VpsDesk.Desktop.Services;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IServerProbeService _probe;
    private readonly ServerProfileStore _store;
    private ServerProfile? _server;
    private string? _activeSecret;
    private Guid? _editingServerId;

    public ObservableCollection<ServerProfile> Servers { get; } = new();
    public ObservableCollection<double> CpuSeries { get; } = new();
    public ObservableCollection<double> MemorySeries { get; } = new();

    public IReadOnlyList<string> AuthenticationOptions { get; } = ["PrivateKey", "Password"];
    public IReadOnlyList<string> EnvironmentOptions { get; } = ["Development", "Staging", "Production"];

    public IEnumerable<ISeries> CpuGaugeSeries => GaugeGenerator.BuildSolidGauge(new GaugeItem(CpuUsage));
    public IEnumerable<ISeries> DiskGaugeSeries => GaugeGenerator.BuildSolidGauge(new GaugeItem(DiskUsage));

    [ObservableProperty] private string _selectedServerName = "No server selected";
    [ObservableProperty] private string _connectionStatus = "Not checked";
    [ObservableProperty] private string _compatibilityLabel = "Compatibility unknown";
    [ObservableProperty] private string _dockerStatus = "Unknown";
    [ObservableProperty] private string _nginxStatus = "Unknown";
    [ObservableProperty] private string _lastChecked = "Never";
    [ObservableProperty] private string _loadSummary = "Load -- / -- / --";
    [ObservableProperty] private string _memorySummary = "Awaiting connection";
    [ObservableProperty] private string _networkSummary = "Network RX/TX --";
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private double _cpuUsage;
    [ObservableProperty] private double _memoryUsage;
    [ObservableProperty] private double _diskUsage;
    [ObservableProperty] private string _uptime = "--";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isRefreshing;

    [ObservableProperty] private bool _autoRefreshEnabled;
    [ObservableProperty] private string _selectedPage = "Dashboard";
    [ObservableProperty] private string _headerTitle = "Dashboard";
    [ObservableProperty] private string _headerSubtitle = "Overview of the selected Linux VPS";
    [ObservableProperty] private ServerProfile? _selectedServerInList;

    [ObservableProperty] private string _editorName = string.Empty;
    [ObservableProperty] private string _editorHost = string.Empty;
    [ObservableProperty] private int _editorPort = 22;
    [ObservableProperty] private string _editorUsername = "root";
    [ObservableProperty] private string _editorAuthenticationType = "PrivateKey";
    [ObservableProperty] private string _editorPrivateKeyPath = string.Empty;
    [ObservableProperty] private string _editorSecret = string.Empty;
    [ObservableProperty] private string _editorProvider = "Hostinger";
    [ObservableProperty] private string _editorEnvironment = "Production";
    [ObservableProperty] private string _editorHostKeyFingerprint = string.Empty;
    [ObservableProperty] private bool _isTestingServer;
    [ObservableProperty] private string _serverEditorMessage = "Secrets are kept in memory only and are not written to servers.json.";

    public bool IsDashboardPage => SelectedPage == "Dashboard";
    public bool IsServersPage => SelectedPage == "Servers";
    public bool IsPlaceholderPage => !IsDashboardPage && !IsServersPage;

    public MainWindowViewModel(
        IServerProbeService probe,
        ServerProfileStore store,
        BootstrapServerContext bootstrap)
    {
        _probe = probe;
        _store = store;

        var snapshot = store.Load();
        foreach (var server in snapshot.Servers)
        {
            Servers.Add(server);
        }

        if (Servers.Count == 0 && bootstrap.Profile != null)
        {
            Servers.Add(bootstrap.Profile);
            _activeSecret = bootstrap.Secret;
            store.Save(Servers, bootstrap.Profile.Id);
        }

        _server = snapshot.SelectedServerId is Guid selectedId
            ? Servers.FirstOrDefault(x => x.Id == selectedId)
            : Servers.FirstOrDefault();

        if (_server == null && bootstrap.Profile != null)
        {
            _server = Servers.FirstOrDefault(x => x.Id == bootstrap.Profile.Id) ?? bootstrap.Profile;
            _activeSecret = bootstrap.Secret;
        }
        else if (_server?.Id == bootstrap.Profile?.Id)
        {
            _activeSecret = bootstrap.Secret;
        }

        if (_server != null)
        {
            SelectedServerName = _server.Name;
            CompatibilityLabel = ProviderPendingLabel(_server);
            SelectedServerInList = _server;
            LoadEditor(_server);
        }

        if (!string.IsNullOrWhiteSpace(bootstrap.Warning) && _server == null)
        {
            StatusMessage = bootstrap.Warning;
        }

        for (var i = 0; i < 30; i++)
        {
            CpuSeries.Add(0);
            MemorySeries.Add(0);
        }
    }

    partial void OnCpuUsageChanged(double value) => OnPropertyChanged(nameof(CpuGaugeSeries));
    partial void OnDiskUsageChanged(double value) => OnPropertyChanged(nameof(DiskGaugeSeries));

    partial void OnSelectedPageChanged(string value)
    {
        OnPropertyChanged(nameof(IsDashboardPage));
        OnPropertyChanged(nameof(IsServersPage));
        OnPropertyChanged(nameof(IsPlaceholderPage));
    }

    partial void OnSelectedServerInListChanged(ServerProfile? value)
    {
        if (value != null)
        {
            LoadEditor(value);
        }
    }

    [RelayCommand]
    private void Navigate(string? page)
    {
        if (string.IsNullOrWhiteSpace(page)) return;
        SelectedPage = page;
        HeaderTitle = page;
        HeaderSubtitle = page switch
        {
            "Servers" => "Manage Linux VPS connection profiles",
            "Dashboard" => "Overview of the selected Linux VPS",
            "Containers" => "Docker and Compose operations",
            "Deployments" => "Safe application deployment workflows",
            "Logs" => "Docker, system and application logs",
            "Storage" => "Disk, images, volumes and large directories",
            "Files" => "Secure SFTP file operations",
            "Terminal" => "Interactive SSH administration",
            "Security" => "Server hardening and exposure checks",
            "Settings" => "VPS Desk preferences and local security",
            _ => "VPS Desk"
        };
    }

    [RelayCommand]
    private void NewServer()
    {
        _editingServerId = null;
        SelectedServerInList = null;
        EditorName = string.Empty;
        EditorHost = string.Empty;
        EditorPort = 22;
        EditorUsername = "root";
        EditorAuthenticationType = "PrivateKey";
        EditorPrivateKeyPath = string.Empty;
        EditorSecret = string.Empty;
        EditorProvider = "Hostinger";
        EditorEnvironment = "Production";
        EditorHostKeyFingerprint = string.Empty;
        ServerEditorMessage = "Create a profile. Passwords/passphrases are session-only for now.";
    }

    [RelayCommand]
    private void SaveServer()
    {
        var id = _editingServerId ?? Guid.NewGuid();
        if (!TryBuildEditorProfile(id, out var profile, out var error))
        {
            ServerEditorMessage = error;
            return;
        }

        var existing = Servers.FirstOrDefault(x => x.Id == id);
        if (existing == null)
        {
            Servers.Add(profile!);
        }
        else
        {
            var index = Servers.IndexOf(existing);
            Servers[index] = profile!;
            if (_server?.Id == id)
            {
                _server = profile;
                ApplyActiveServerHeader();
            }
        }

        _editingServerId = id;
        SelectedServerInList = profile;
        _store.Save(Servers, _server?.Id);
        ServerEditorMessage = string.IsNullOrWhiteSpace(profile!.HostKeyFingerprintSha256)
            ? "Profile saved locally. Secret was not persisted. Add the server SHA256 fingerprint to pin its identity."
            : "Profile saved locally with SSH host-key pinning. Secret was not persisted.";
    }

    [RelayCommand]
    private async Task TestServerAsync()
    {
        var id = _editingServerId ?? Guid.NewGuid();
        if (!TryBuildEditorProfile(id, out var profile, out var error))
        {
            ServerEditorMessage = error;
            return;
        }

        IsTestingServer = true;
        ServerEditorMessage = "Testing SSH connection and Linux capabilities...";
        try
        {
            var secret = string.IsNullOrWhiteSpace(EditorSecret) ? null : EditorSecret;
            var result = await _probe.CheckCompatibilityAsync(profile!, secret);
            var ssh = result.Capabilities.FirstOrDefault(x => x.Capability == ServerCapability.Ssh);
            if (ssh?.Available != true)
            {
                ServerEditorMessage = $"SSH failed: {ssh?.Detail ?? "connection or authentication rejected"}";
                return;
            }

            var detected = result.Capabilities
                .Where(x => x.Available && x.Capability is not ServerCapability.Ssh and not ServerCapability.Linux)
                .Select(x => x.Capability.ToString())
                .ToArray();

            ServerEditorMessage = $"Connection OK · {result.Distribution ?? "Linux"} · {result.Level} · " +
                                  (detected.Length == 0 ? "basic SSH only" : string.Join(", ", detected));
        }
        catch (Exception ex)
        {
            ServerEditorMessage = $"Connection test failed: {ex.Message}";
        }
        finally
        {
            IsTestingServer = false;
        }
    }

    [RelayCommand]
    private void DeleteServer()
    {
        if (SelectedServerInList == null) return;
        var deleting = SelectedServerInList;
        Servers.Remove(deleting);
        SelectedServerInList = null;

        if (_server?.Id == deleting.Id)
        {
            _server = Servers.FirstOrDefault();
            _activeSecret = null;
            AutoRefreshEnabled = false;
            ApplyActiveServerHeader();
        }

        _store.Save(Servers, _server?.Id);
        NewServer();
        ServerEditorMessage = "Profile deleted.";
    }

    [RelayCommand]
    private void ActivateServer()
    {
        if (SelectedServerInList == null)
        {
            ServerEditorMessage = "Select a saved server first.";
            return;
        }

        _server = SelectedServerInList;
        _activeSecret = string.IsNullOrWhiteSpace(EditorSecret) ? null : EditorSecret;
        AutoRefreshEnabled = false;
        _store.Save(Servers, _server.Id);
        ApplyActiveServerHeader();
        SelectedPage = "Dashboard";
        HeaderTitle = "Dashboard";
        HeaderSubtitle = "Overview of the selected Linux VPS";
        StatusMessage = "Server activated. Press Refresh to run compatibility checks and telemetry.";
        RefreshCommand.NotifyCanExecuteChanged();
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
            var compatibility = await _probe.CheckCompatibilityAsync(_server, _activeSecret);
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
                AutoRefreshEnabled = false;
                StatusMessage = ssh?.Detail ?? "SSH connection failed.";
                LastChecked = DateTimeOffset.Now.ToString("HH:mm:ss");
                return;
            }

            StatusMessage = "Reading live telemetry...";
            var metrics = await _probe.ReadMetricsAsync(_server, _activeSecret);

            CpuUsage = Math.Clamp(metrics.CpuUsagePercent, 0, 100);
            MemoryUsage = Math.Clamp(metrics.MemoryUsagePercent, 0, 100);
            DiskUsage = Math.Clamp(metrics.DiskUsagePercent, 0, 100);
            Uptime = FormatUptime(metrics.Uptime);
            LoadSummary = $"Load {metrics.Load1:F2} / {metrics.Load5:F2} / {metrics.Load15:F2}";
            MemorySummary = $"{FormatBytes(metrics.MemoryUsedBytes)} / {FormatBytes(metrics.MemoryTotalBytes)}";
            NetworkSummary = $"RX {FormatRate(metrics.NetworkRxBytesPerSecond)} · TX {FormatRate(metrics.NetworkTxBytesPerSecond)}";

            Push(CpuSeries, CpuUsage, 60);
            Push(MemorySeries, MemoryUsage, 60);

            AutoRefreshEnabled = true;
            LastChecked = DateTimeOffset.Now.ToString("HH:mm:ss");
            StatusMessage = compatibility.Distribution is { Length: > 0 }
                ? $"{compatibility.Distribution} · {compatibility.Kernel}"
                : "Telemetry updated.";
        }
        catch (Exception ex)
        {
            AutoRefreshEnabled = false;
            ConnectionStatus = "SSH Offline";
            StatusMessage = ex.Message;
            LastChecked = DateTimeOffset.Now.ToString("HH:mm:ss");
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private bool TryBuildEditorProfile(Guid id, out ServerProfile? profile, out string error)
    {
        profile = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(EditorName) || string.IsNullOrWhiteSpace(EditorHost) || string.IsNullOrWhiteSpace(EditorUsername))
        {
            error = "Name, host and username are required.";
            return false;
        }

        if (EditorPort is < 1 or > 65535)
        {
            error = "SSH port must be between 1 and 65535.";
            return false;
        }

        if (!Enum.TryParse<SshAuthenticationType>(EditorAuthenticationType, out var auth) || auth == SshAuthenticationType.Agent)
        {
            auth = SshAuthenticationType.PrivateKey;
        }

        if (!Enum.TryParse<ServerEnvironment>(EditorEnvironment, out var environment))
        {
            environment = ServerEnvironment.Production;
        }

        if (auth == SshAuthenticationType.PrivateKey && string.IsNullOrWhiteSpace(EditorPrivateKeyPath))
        {
            error = "Select or enter a private key path for key authentication.";
            return false;
        }

        profile = new ServerProfile(
            id,
            EditorName.Trim(),
            EditorHost.Trim(),
            EditorPort,
            EditorUsername.Trim(),
            auth,
            auth == SshAuthenticationType.PrivateKey ? EditorPrivateKeyPath.Trim() : null,
            null,
            string.IsNullOrWhiteSpace(EditorProvider) ? null : EditorProvider.Trim(),
            environment,
            [],
            string.IsNullOrWhiteSpace(EditorHostKeyFingerprint) ? null : EditorHostKeyFingerprint.Trim());
        return true;
    }

    private void LoadEditor(ServerProfile server)
    {
        _editingServerId = server.Id;
        EditorName = server.Name;
        EditorHost = server.Host;
        EditorPort = server.Port;
        EditorUsername = server.Username;
        EditorAuthenticationType = server.AuthenticationType.ToString();
        EditorPrivateKeyPath = server.PrivateKeyPath ?? string.Empty;
        EditorSecret = server.Id == _server?.Id ? _activeSecret ?? string.Empty : string.Empty;
        EditorProvider = server.ProviderLabel ?? string.Empty;
        EditorEnvironment = server.Environment.ToString();
        EditorHostKeyFingerprint = server.HostKeyFingerprintSha256 ?? string.Empty;
        ServerEditorMessage = "Edit, test, save or activate this profile. Secrets are not persisted.";
    }

    private void ApplyActiveServerHeader()
    {
        if (_server == null)
        {
            SelectedServerName = "No server selected";
            ConnectionStatus = "Offline";
            CompatibilityLabel = "Compatibility unknown";
            return;
        }

        SelectedServerName = _server.Name;
        ConnectionStatus = "Not checked";
        CompatibilityLabel = ProviderPendingLabel(_server);
    }

    private static string ProviderPendingLabel(ServerProfile server)
        => string.Equals(server.ProviderLabel, "Hostinger", StringComparison.OrdinalIgnoreCase)
            ? "HOSTINGER · PENDING CHECK"
            : "GENERIC LINUX · PENDING CHECK";

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
