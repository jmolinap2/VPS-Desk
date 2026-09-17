using Avalonia;
using Avalonia.Controls;
using VpsDesk.Application.Abstractions;
using VpsDesk.Domain.Servers;
using VpsDesk.Infrastructure.Logs;
using VpsDesk.Infrastructure.Persistence;
using VpsDesk.Infrastructure.Ssh;

namespace VpsDesk.Android;

internal sealed class AndroidLogsView : UserControl
{
    private readonly IServerProfileStore _profileStore = new JsonServerProfileStore(AndroidAppPaths.ServerProfilesFile);
    private readonly ISecretStore _secretStore = new AndroidKeystoreSecretStore();
    private readonly IRemoteLogService _logs = new LinuxRemoteLogService(new SshNetCommandExecutor());

    private readonly ComboBox _servicePicker = new();
    private readonly TextBox _logOutput = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
        MinHeight = 360
    };
    private readonly TextBlock _status = new()
    {
        Text = "Select an active server first.",
        TextWrapping = Avalonia.Media.TextWrapping.Wrap
    };
    private readonly Avalonia.Controls.Button _servicesButton = new() { Content = "Load services" };
    private readonly Avalonia.Controls.Button _logsButton = new() { Content = "Read logs" };
    private readonly Avalonia.Controls.Button _cancelButton = new() { Content = "Cancel", IsEnabled = false };

    private CancellationTokenSource? _operationCts;

    public AndroidLogsView()
    {
        _servicesButton.Click += async (_, _) => await LoadServicesAsync();
        _logsButton.Click += async (_, _) => await ReadLogsAsync();
        _cancelButton.Click += (_, _) => _operationCts?.Cancel();

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Logs", FontSize = 28 },
                    new TextBlock
                    {
                        Text = "Read-only systemd journal viewer. Up to 100 recent lines are requested.",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    _servicePicker,
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,*,*"),
                        ColumnSpacing = 8,
                        Children =
                        {
                            Place(_servicesButton, 0),
                            Place(_logsButton, 1),
                            Place(_cancelButton, 2)
                        }
                    },
                    _status,
                    _logOutput
                }
            }
        };
    }

    private async Task LoadServicesAsync()
    {
        var context = await ResolveServerAsync();
        if (context is null)
        {
            return;
        }

        var token = BeginOperation(TimeSpan.FromSeconds(20));
        _status.Text = $"Loading services from {context.Value.Server.Name}...";
        try
        {
            var services = await _logs.ListSystemdServicesAsync(
                context.Value.Server,
                context.Value.Secret,
                token);

            _servicePicker.ItemsSource = services;
            if (services.Count > 0)
            {
                _servicePicker.SelectedIndex = 0;
            }

            _status.Text = $"{services.Count} systemd service(s) available.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Service discovery cancelled or timed out.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Could not load services: {ex.Message}";
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task ReadLogsAsync()
    {
        if (_servicePicker.SelectedItem is not string serviceName || string.IsNullOrWhiteSpace(serviceName))
        {
            _status.Text = "Select a systemd service first.";
            return;
        }

        var context = await ResolveServerAsync();
        if (context is null)
        {
            return;
        }

        var token = BeginOperation(TimeSpan.FromSeconds(30));
        _status.Text = $"Reading {serviceName}...";
        try
        {
            _logOutput.Text = await _logs.ReadSystemdLogsAsync(
                context.Value.Server,
                context.Value.Secret,
                serviceName,
                100,
                token);
            _status.Text = $"Loaded recent logs for {serviceName}.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Log read cancelled or timed out.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Could not read logs: {ex.Message}";
        }
        finally
        {
            EndOperation();
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
            ? null
            : await _secretStore.GetAsync(active.SecretReference);

        if (string.IsNullOrWhiteSpace(secret) && active.AuthenticationType == SshAuthenticationType.Password)
        {
            _status.Text = "This server has no remembered password. Enter it from Servers first.";
            return null;
        }

        return (active, secret);
    }

    private CancellationToken BeginOperation(TimeSpan timeout)
    {
        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource(timeout);
        _servicesButton.IsEnabled = false;
        _logsButton.IsEnabled = false;
        _cancelButton.IsEnabled = true;
        return _operationCts.Token;
    }

    private void EndOperation()
    {
        _operationCts?.Dispose();
        _operationCts = null;
        _servicesButton.IsEnabled = true;
        _logsButton.IsEnabled = true;
        _cancelButton.IsEnabled = false;
    }

    private static Control Place(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }
}
