using Avalonia;
using Avalonia.Controls;
using VpsDesk.Application.Abstractions;
using VpsDesk.Domain.Servers;
using VpsDesk.Infrastructure.Monitoring;
using VpsDesk.Infrastructure.Persistence;
using VpsDesk.Infrastructure.Ssh;

namespace VpsDesk.Android;

internal sealed class AndroidDashboardView : UserControl
{
    private readonly IServerProfileStore _profileStore = new JsonServerProfileStore(AndroidAppPaths.ServerProfilesFile);
    private readonly ISecretStore _secretStore = new AndroidKeystoreSecretStore();
    private readonly IServerProbeService _probe = new LinuxServerProbeService(new SshNetCommandExecutor());

    private readonly TextBlock _serverName = new() { Text = "No active server", FontSize = 22 };
    private readonly TextBlock _cpu = new() { Text = "CPU —" };
    private readonly TextBlock _memory = new() { Text = "Memory —" };
    private readonly TextBlock _disk = new() { Text = "Disk —" };
    private readonly TextBlock _load = new() { Text = "Load —" };
    private readonly TextBlock _network = new() { Text = "Network —" };
    private readonly TextBlock _uptime = new() { Text = "Uptime —" };
    private readonly TextBlock _status = new()
    {
        Text = "Select a server to load metrics.",
        TextWrapping = Avalonia.Media.TextWrapping.Wrap
    };
    private readonly Avalonia.Controls.Button _refreshButton = new() { Content = "Refresh" };
    private readonly Avalonia.Controls.Button _cancelButton = new() { Content = "Cancel", IsEnabled = false };

    private CancellationTokenSource? _refreshCts;

    public AndroidDashboardView()
    {
        _refreshButton.Click += async (_, _) => await RefreshAsync();
        _cancelButton.Click += (_, _) => _refreshCts?.Cancel();
        AttachedToVisualTree += async (_, _) => await RefreshAsync();

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Dashboard", FontSize = 28 },
                    _serverName,
                    MetricCard("Processor", _cpu),
                    MetricCard("Memory", _memory),
                    MetricCard("Disk", _disk),
                    MetricCard("Load average", _load),
                    MetricCard("Network", _network),
                    MetricCard("Uptime", _uptime),
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,*"),
                        ColumnSpacing = 8,
                        Children =
                        {
                            Place(_refreshButton, 0),
                            Place(_cancelButton, 1)
                        }
                    },
                    _status
                }
            }
        };
    }

    private async Task RefreshAsync()
    {
        if (_refreshCts is not null)
        {
            return;
        }

        var snapshot = await _profileStore.LoadAsync();
        var active = snapshot.SelectedServerId is Guid selectedId
            ? snapshot.Servers.FirstOrDefault(x => x.Id == selectedId)
            : null;

        if (active is null)
        {
            _serverName.Text = "No active server";
            _status.Text = "Open Servers and select or save a server first.";
            ClearMetrics();
            return;
        }

        _serverName.Text = active.Name;
        _status.Text = $"Reading {active.Host}...";
        _refreshButton.IsEnabled = false;
        _cancelButton.IsEnabled = true;
        _refreshCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        try
        {
            var secret = string.IsNullOrWhiteSpace(active.SecretReference)
                ? null
                : await _secretStore.GetAsync(active.SecretReference, _refreshCts.Token);

            if (string.IsNullOrWhiteSpace(secret) && active.AuthenticationType == SshAuthenticationType.Password)
            {
                _status.Text = "This server has no remembered password. Enter it from Servers before refreshing.";
                return;
            }

            var metrics = await _probe.ReadMetricsAsync(active, secret, _refreshCts.Token);
            _cpu.Text = $"CPU {metrics.CpuPercent:N1}%";
            _memory.Text = $"Memory {FormatBytes(metrics.MemoryUsedBytes)} / {FormatBytes(metrics.MemoryTotalBytes)}";
            _disk.Text = $"Disk {FormatBytes(metrics.DiskUsedBytes)} / {FormatBytes(metrics.DiskTotalBytes)}";
            _load.Text = $"Load {metrics.Load1:N2} / {metrics.Load5:N2} / {metrics.Load15:N2}";
            _network.Text = $"Network ↓ {FormatRate(metrics.NetworkReceiveBytesPerSecond)}  ↑ {FormatRate(metrics.NetworkTransmitBytesPerSecond)}";
            _uptime.Text = $"Uptime {FormatDuration(metrics.Uptime)}";
            _status.Text = $"Updated {metrics.CapturedAt.ToLocalTime():HH:mm:ss}.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Dashboard refresh cancelled or timed out.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Could not read metrics: {ex.Message}";
        }
        finally
        {
            _refreshCts?.Dispose();
            _refreshCts = null;
            _refreshButton.IsEnabled = true;
            _cancelButton.IsEnabled = false;
        }
    }

    private void ClearMetrics()
    {
        _cpu.Text = "CPU —";
        _memory.Text = "Memory —";
        _disk.Text = "Disk —";
        _load.Text = "Load —";
        _network.Text = "Network —";
        _uptime.Text = "Uptime —";
    }

    private static Border MetricCard(string title, TextBlock value)
        => new()
        {
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(12),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = title, Opacity = 0.7 },
                    value
                }
            }
        };

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:N1} {units[unit]}";
    }

    private static string FormatRate(double bytesPerSecond)
        => $"{FormatBytes((long)Math.Max(0, bytesPerSecond))}/s";

    private static string FormatDuration(TimeSpan uptime)
        => uptime.TotalDays >= 1
            ? $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m"
            : $"{uptime.Hours}h {uptime.Minutes}m";

    private static Control Place(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }
}
