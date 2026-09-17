using Avalonia;
using Avalonia.Controls;
using VpsDesk.Application.Abstractions;
using VpsDesk.Domain.Containers;
using VpsDesk.Domain.Servers;
using VpsDesk.Infrastructure.Docker;
using VpsDesk.Infrastructure.Persistence;
using VpsDesk.Infrastructure.Ssh;

namespace VpsDesk.Android;

internal sealed class AndroidContainersView : UserControl
{
    private readonly IServerProfileStore _profileStore = new JsonServerProfileStore(AndroidAppPaths.ServerProfilesFile);
    private readonly ISecretStore _secretStore = new AndroidKeystoreSecretStore();
    private readonly IContainerService _containers = new DockerContainerService(new SshNetCommandExecutor());

    private readonly StackPanel _list = new() { Spacing = 10 };
    private readonly TextBlock _status = new()
    {
        Text = "Select a server to load containers.",
        TextWrapping = Avalonia.Media.TextWrapping.Wrap
    };
    private readonly Avalonia.Controls.Button _refreshButton = new() { Content = "Refresh containers" };
    private readonly Avalonia.Controls.Button _cancelButton = new() { Content = "Cancel", IsEnabled = false };

    private CancellationTokenSource? _loadCts;

    public AndroidContainersView()
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
                    new TextBlock { Text = "Containers", FontSize = 28 },
                    new TextBlock
                    {
                        Text = "Read-only Docker inventory. Container actions remain disabled until Android SSH is proven on a physical device.",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
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
                    _status,
                    _list
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
            _list.Children.Clear();
            _status.Text = "Open Servers and select or save a server first.";
            return;
        }

        _refreshButton.IsEnabled = false;
        _cancelButton.IsEnabled = true;
        _status.Text = $"Reading containers from {active.Name}...";
        _loadCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            var secret = string.IsNullOrWhiteSpace(active.SecretReference)
                ? null
                : await _secretStore.GetAsync(active.SecretReference, _loadCts.Token);

            if (string.IsNullOrWhiteSpace(secret) && active.AuthenticationType == SshAuthenticationType.Password)
            {
                _list.Children.Clear();
                _status.Text = "This server has no remembered password. Enter it from Servers before refreshing.";
                return;
            }

            var containers = await _containers.ListAsync(active, secret, _loadCts.Token);
            Render(containers);
            _status.Text = $"{containers.Count} container(s) loaded.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Container refresh cancelled or timed out.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Could not load containers: {ex.Message}";
        }
        finally
        {
            _loadCts?.Dispose();
            _loadCts = null;
            _refreshButton.IsEnabled = true;
            _cancelButton.IsEnabled = false;
        }
    }

    private void Render(IReadOnlyList<DockerContainerInfo> containers)
    {
        _list.Children.Clear();

        if (containers.Count == 0)
        {
            _list.Children.Add(new TextBlock { Text = "No Docker containers found." });
            return;
        }

        foreach (var container in containers)
        {
            var project = string.IsNullOrWhiteSpace(container.ComposeProject)
                ? string.Empty
                : $" · {container.ComposeProject}";

            _list.Children.Add(new Border
            {
                Padding = new Thickness(14),
                CornerRadius = new CornerRadius(12),
                Child = new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock { Text = container.Name, FontSize = 18 },
                        new TextBlock
                        {
                            Text = $"{container.State}{project}",
                            Opacity = 0.75
                        },
                        new TextBlock
                        {
                            Text = $"CPU {container.CpuPercent:N1}% · MEM {container.MemoryUsage} ({container.MemoryPercent:N1}%)"
                        },
                        new TextBlock
                        {
                            Text = $"NET {container.NetworkIo}",
                            Opacity = 0.75
                        },
                        new TextBlock
                        {
                            Text = container.Status,
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                            Opacity = 0.75
                        }
                    }
                }
            });
        }
    }

    private static Control Place(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }
}
