using Avalonia;
using Avalonia.Controls;
using VpsDesk.Application.Abstractions;
using VpsDesk.Domain.Files;
using VpsDesk.Domain.Servers;
using VpsDesk.Infrastructure.Persistence;
using VpsDesk.Infrastructure.Ssh;

namespace VpsDesk.Android;

internal sealed class AndroidFilesView : UserControl
{
    private readonly IServerProfileStore _profileStore = new JsonServerProfileStore(AndroidAppPaths.ServerProfilesFile);
    private readonly ISecretStore _secretStore = new AndroidKeystoreSecretStore();
    private readonly IRemoteFileService _files = new SftpRemoteFileService();

    private readonly TextBox _path = new() { Text = "/", PlaceholderText = "Remote path" };
    private readonly StackPanel _entries = new() { Spacing = 6 };
    private readonly TextBlock _status = new()
    {
        Text = "Select an active server first.",
        TextWrapping = Avalonia.Media.TextWrapping.Wrap
    };
    private readonly Avalonia.Controls.Button _upButton = new() { Content = "Up" };
    private readonly Avalonia.Controls.Button _goButton = new() { Content = "Go" };
    private readonly Avalonia.Controls.Button _cancelButton = new() { Content = "Cancel", IsEnabled = false };

    private CancellationTokenSource? _loadCts;

    public AndroidFilesView()
    {
        _upButton.Click += async (_, _) => await LoadPathAsync(ParentPath(_path.Text));
        _goButton.Click += async (_, _) => await LoadPathAsync(_path.Text);
        _cancelButton.Click += (_, _) => _loadCts?.Cancel();
        AttachedToVisualTree += async (_, _) => await LoadPathAsync("/");

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Files", FontSize = 28 },
                    new TextBlock
                    {
                        Text = "Read-only SFTP browser. File editing and transfer actions remain disabled until runtime validation.",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    _path,
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,*,*"),
                        ColumnSpacing = 8,
                        Children =
                        {
                            Place(_upButton, 0),
                            Place(_goButton, 1),
                            Place(_cancelButton, 2)
                        }
                    },
                    _status,
                    _entries
                }
            }
        };
    }

    private async Task LoadPathAsync(string? requestedPath)
    {
        if (_loadCts is not null)
        {
            return;
        }

        var context = await ResolveServerAsync();
        if (context is null)
        {
            return;
        }

        var remotePath = NormalizePath(requestedPath);
        _loadCts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        SetBusy(true);
        _status.Text = $"Listing {remotePath}...";

        try
        {
            var entries = await _files.ListAsync(
                context.Value.Server,
                remotePath,
                context.Value.Secret,
                _loadCts.Token);

            _path.Text = remotePath;
            Render(entries);
            _status.Text = $"{entries.Count} item(s) in {remotePath}.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "SFTP listing cancelled or timed out.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Could not list {remotePath}: {ex.Message}";
        }
        finally
        {
            _loadCts?.Dispose();
            _loadCts = null;
            SetBusy(false);
        }
    }

    private void Render(IReadOnlyList<RemoteFileEntry> entries)
    {
        _entries.Children.Clear();

        if (entries.Count == 0)
        {
            _entries.Children.Add(new TextBlock { Text = "This directory is empty." });
            return;
        }

        foreach (var entry in entries)
        {
            if (entry.IsDirectory)
            {
                var directoryButton = new Avalonia.Controls.Button
                {
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    Content = $"{entry.Name}/"
                };
                var target = entry.FullPath;
                directoryButton.Click += async (_, _) => await LoadPathAsync(target);
                _entries.Children.Add(directoryButton);
                continue;
            }

            _entries.Children.Add(new Border
            {
                Padding = new Thickness(12, 8),
                CornerRadius = new CornerRadius(10),
                Child = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    ColumnSpacing = 8,
                    Children =
                    {
                        PlaceColumn(
                            new TextBlock
                            {
                                Text = entry.Name,
                                TextWrapping = Avalonia.Media.TextWrapping.Wrap
                            },
                            0),
                        PlaceColumn(
                            new TextBlock
                            {
                                Text = entry.SizeLabel,
                                Opacity = 0.7
                            },
                            1)
                    }
                }
            });
        }
    }

    private async Task<(ServerProfile Server, string? Secret)?> ResolveServerAsync()
    {
        var snapshot = await _profileStore.LoadAsync();
        var active = snapshot.SelectedServerId is Guid selectedId
            ? snapshot.Servers.FirstOrDefault(x => x.Id == selectedId)
            : null;

        if (active is null)
        {
            _status.Text = "Open Servers and select or save a server first.";
            return null;
        }

        var secret = string.IsNullOrWhiteSpace(active.SecretReference)
            ? AndroidRuntimeSecrets.Get(active.Id)
            : await _secretStore.GetAsync(active.SecretReference)
              ?? AndroidRuntimeSecrets.Get(active.Id);

        if (string.IsNullOrWhiteSpace(secret) && active.AuthenticationType == SshAuthenticationType.Password)
        {
            _status.Text = "This server has no remembered password. Enter it from Servers first.";
            return null;
        }

        return (active, secret);
    }

    private void SetBusy(bool busy)
    {
        _upButton.IsEnabled = !busy;
        _goButton.IsEnabled = !busy;
        _path.IsEnabled = !busy;
        _cancelButton.IsEnabled = busy;
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "/";
        path = path.Trim().Replace('\\', '/');
        if (!path.StartsWith('/')) path = "/" + path;
        while (path.Contains("//", StringComparison.Ordinal))
        {
            path = path.Replace("//", "/", StringComparison.Ordinal);
        }

        return path.Length > 1 ? path.TrimEnd('/') : path;
    }

    private static string ParentPath(string? path)
    {
        path = NormalizePath(path);
        if (path == "/") return "/";

        var separator = path.LastIndexOf('/');
        return separator <= 0 ? "/" : path[..separator];
    }

    private static Control Place(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private static Control PlaceColumn(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }
}
