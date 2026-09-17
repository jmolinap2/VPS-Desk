using Avalonia;
using Avalonia.Controls;
using VpsDesk.Application.Abstractions;
using VpsDesk.Domain.Servers;
using VpsDesk.Domain.Storage;
using VpsDesk.Infrastructure.Persistence;
using VpsDesk.Infrastructure.Ssh;
using VpsDesk.Infrastructure.Storage;

namespace VpsDesk.Android;

internal sealed class AndroidStorageView : UserControl
{
    private readonly IServerProfileStore _profileStore = new JsonServerProfileStore(AndroidAppPaths.ServerProfilesFile);
    private readonly ISecretStore _secretStore = new AndroidKeystoreSecretStore();
    private readonly IStorageService _storage = new LinuxStorageService(new SshNetCommandExecutor());

    private readonly TextBlock _rootUsage = new() { Text = "Root filesystem —", FontSize = 20 };
    private readonly StackPanel _dockerUsage = new() { Spacing = 6 };
    private readonly StackPanel _heavyDirectories = new() { Spacing = 6 };
    private readonly TextBlock _status = new()
    {
        Text = "Select a server to load storage.",
        TextWrapping = Avalonia.Media.TextWrapping.Wrap
    };
    private readonly Avalonia.Controls.Button _refreshButton = new() { Content = "Refresh storage" };
    private readonly Avalonia.Controls.Button _cancelButton = new() { Content = "Cancel", IsEnabled = false };

    private CancellationTokenSource? _loadCts;

    public AndroidStorageView()
    {
        _refreshButton.Click += async (_, _) => await RefreshAsync();
        _cancelButton.Click += (_, _) => _loadCts?.Cancel();
        AttachedToVisualTree += async (_, _) => await RefreshAsync();

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Storage", FontSize = 28 },
                    new TextBlock
                    {
                        Text = "Read-only storage analysis. Cleanup actions remain unavailable on Android until runtime validation is complete.",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    _rootUsage,
                    new TextBlock { Text = "Docker usage", FontSize = 18 },
                    _dockerUsage,
                    new TextBlock { Text = "Largest directories", FontSize = 18 },
                    _heavyDirectories,
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
        if (_loadCts is not null)
        {
            return;
        }

        var snapshot = await _profileStore.LoadAsync();
        var active = snapshot.SelectedServerId is Guid selectedId
            ? snapshot.Servers.FirstOrDefault(x => x.Id == selectedId)
            : null;

        if (active is null)
        {
            _status.Text = "Open Servers and select or save a server first.";
            Clear();
            return;
        }

        _loadCts = new CancellationTokenSource(TimeSpan.FromSeconds(55));
        _refreshButton.IsEnabled = false;
        _cancelButton.IsEnabled = true;
        _status.Text = $"Reading storage from {active.Name}...";

        try
        {
            var secret = string.IsNullOrWhiteSpace(active.SecretReference)
                ? null
                : await _secretStore.GetAsync(active.SecretReference, _loadCts.Token);

            if (string.IsNullOrWhiteSpace(secret) && active.AuthenticationType == SshAuthenticationType.Password)
            {
                _status.Text = "This server has no remembered password. Enter it from Servers first.";
                Clear();
                return;
            }

            var storage = await _storage.ReadAsync(active, secret, _loadCts.Token);
            Render(storage);
            _status.Text = $"Updated {storage.CollectedAtUtc.ToLocalTime():HH:mm:ss}.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Storage refresh cancelled or timed out.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Could not read storage: {ex.Message}";
        }
        finally
        {
            _loadCts?.Dispose();
            _loadCts = null;
            _refreshButton.IsEnabled = true;
            _cancelButton.IsEnabled = false;
        }
    }

    private void Render(StorageSnapshot storage)
    {
        _rootUsage.Text =
            $"Root {FormatBytes(storage.RootUsedBytes)} / {FormatBytes(storage.RootTotalBytes)} ({storage.RootUsagePercent:N1}%)";

        _dockerUsage.Children.Clear();
        if (storage.DockerUsage.Count == 0)
        {
            _dockerUsage.Children.Add(new TextBlock { Text = "Docker usage unavailable." });
        }
        else
        {
            foreach (var item in storage.DockerUsage)
            {
                _dockerUsage.Children.Add(new TextBlock
                {
                    Text = $"{item.Type}: {item.Size} · reclaimable {item.Reclaimable}",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                });
            }
        }

        _heavyDirectories.Children.Clear();
        if (storage.HeavyDirectories.Count == 0)
        {
            _heavyDirectories.Children.Add(new TextBlock { Text = "No directory usage data available." });
        }
        else
        {
            foreach (var directory in storage.HeavyDirectories.Take(10))
            {
                _heavyDirectories.Children.Add(new TextBlock
                {
                    Text = $"{directory.SizeLabel}  {directory.Path}",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                });
            }
        }
    }

    private void Clear()
    {
        _rootUsage.Text = "Root filesystem —";
        _dockerUsage.Children.Clear();
        _heavyDirectories.Children.Clear();
    }

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

    private static Control Place(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }
}
