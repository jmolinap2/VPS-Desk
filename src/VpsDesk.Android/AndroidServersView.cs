using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using VpsDesk.Application.Abstractions;
using VpsDesk.Domain.Servers;
using VpsDesk.Infrastructure.Persistence;
using VpsDesk.Infrastructure.Ssh;

namespace VpsDesk.Android;

internal sealed class AndroidServersView : UserControl
{
    private readonly IServerProfileStore _profileStore = new JsonServerProfileStore(AndroidAppPaths.ServerProfilesFile);
    private readonly ISecretStore _secretStore = new AndroidKeystoreSecretStore();
    private readonly ISshCommandExecutor _ssh = new SshNetCommandExecutor();
    private readonly IRemoteFileService _remoteFiles = new SftpRemoteFileService();

    private readonly ComboBox _serverPicker = new();
    private readonly TextBox _name = new() { Watermark = "Name" };
    private readonly TextBox _host = new() { Watermark = "Host or IP" };
    private readonly TextBox _port = new() { Text = "22", Watermark = "Port" };
    private readonly TextBox _username = new() { Watermark = "Username" };
    private readonly TextBox _password = new() { Watermark = "Password", PasswordChar = '●' };
    private readonly TextBox _fingerprint = new() { Watermark = "Optional SHA256 host fingerprint" };
    private readonly Avalonia.Controls.CheckBox _rememberPassword = new() { Content = "Remember password securely" };
    private readonly TextBlock _status = new() { Text = "Loading server profiles...", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly Avalonia.Controls.Button _saveButton = new() { Content = "Save server" };
    private readonly Avalonia.Controls.Button _testButton = new() { Content = "Test SSH" };
    private readonly Avalonia.Controls.Button _sftpButton = new() { Content = "Test SFTP" };
    private readonly Avalonia.Controls.Button _newButton = new() { Content = "New" };

    private List<ServerProfile> _profiles = [];
    private Guid? _editingId;
    private bool _loadingSelection;

    public AndroidServersView()
    {
        _serverPicker.SelectionChanged += async (_, _) => await LoadSelectedProfileAsync();
        _saveButton.Click += async (_, _) => await SaveAsync();
        _testButton.Click += async (_, _) => await TestAsync();
        _sftpButton.Click += async (_, _) => await TestSftpAsync();
        _newButton.Click += (_, _) => ResetEditor();
        AttachedToVisualTree += async (_, _) => await LoadProfilesAsync();

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Servers", FontSize = 28 },
                    new TextBlock
                    {
                        Text = "Profiles are stored locally. Remembered passwords are encrypted with Android Keystore and are not written to servers.json.",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    _serverPicker,
                    _name,
                    _host,
                    _port,
                    _username,
                    _password,
                    _fingerprint,
                    _rememberPassword,
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,*,*"),
                        ColumnSpacing = 8,
                        Children =
                        {
                            Place(_newButton, 0),
                            Place(_saveButton, 1),
                            Place(_testButton, 2)
                        }
                    },
                    _sftpButton,
                    _status
                }
            }
        };
    }

    private async Task LoadProfilesAsync()
    {
        var snapshot = await _profileStore.LoadAsync();
        _profiles = snapshot.Servers.ToList();
        RefreshPicker();

        var selectedIndex = snapshot.SelectedServerId is Guid selectedId
            ? _profiles.FindIndex(x => x.Id == selectedId)
            : -1;

        if (selectedIndex < 0 && _profiles.Count > 0)
        {
            selectedIndex = 0;
        }

        if (selectedIndex >= 0)
        {
            _serverPicker.SelectedIndex = selectedIndex;
        }
        else
        {
            ResetEditor();
        }

        _status.Text = _profiles.Count == 0
            ? "No saved servers yet."
            : $"Loaded {_profiles.Count} server profile(s).";
    }

    private async Task LoadSelectedProfileAsync()
    {
        if (_loadingSelection || _serverPicker.SelectedIndex < 0 || _serverPicker.SelectedIndex >= _profiles.Count)
        {
            return;
        }

        var profile = _profiles[_serverPicker.SelectedIndex];
        _editingId = profile.Id;
        _name.Text = profile.Name;
        _host.Text = profile.Host;
        _port.Text = profile.Port.ToString();
        _username.Text = profile.Username;
        _fingerprint.Text = profile.HostKeyFingerprintSha256 ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(profile.SecretReference))
        {
            _password.Text = await _secretStore.GetAsync(profile.SecretReference) ?? string.Empty;
            _rememberPassword.IsChecked = true;
        }
        else
        {
            _password.Text = string.Empty;
            _rememberPassword.IsChecked = false;
        }
    }

    private async Task SaveAsync()
    {
        if (!TryCreateProfile(out var profile, out var error))
        {
            _status.Text = error;
            return;
        }

        SetBusy(true);
        try
        {
            var secretReference = $"server:{profile.Id:N}:password";
            if (_rememberPassword.IsChecked == true && !string.IsNullOrEmpty(_password.Text))
            {
                await _secretStore.SetAsync(secretReference, _password.Text);
                profile = profile with { SecretReference = secretReference };
            }
            else
            {
                await _secretStore.RemoveAsync(secretReference);
                profile = profile with { SecretReference = null };
            }

            var index = _profiles.FindIndex(x => x.Id == profile.Id);
            if (index >= 0)
            {
                _profiles[index] = profile;
            }
            else
            {
                _profiles.Add(profile);
                index = _profiles.Count - 1;
            }

            await _profileStore.SaveAsync(_profiles, profile.Id);
            _editingId = profile.Id;
            RefreshPicker(index);
            _status.Text = "Server saved.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Could not save server: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task TestAsync()
    {
        if (!TryCreateProfile(out var profile, out var error))
        {
            _status.Text = error;
            return;
        }

        SetBusy(true);
        _status.Text = "Connecting...";
        try
        {
            var result = await _ssh.ExecuteAsync(
                new SshCommandRequest(profile, "printf 'VPS_DESK_ANDROID_OK\\n'; uname -s; uname -m", TimeSpan.FromSeconds(15)),
                _password.Text);

            _status.Text = result.Succeeded
                ? $"SSH OK ({result.Duration.TotalMilliseconds:N0} ms)\n{result.StandardOutput.Trim()}"
                : $"SSH failed: {result.StandardError}";
        }
        catch (Exception ex)
        {
            _status.Text = $"SSH failed: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task TestSftpAsync()
    {
        if (!TryCreateProfile(out var profile, out var error))
        {
            _status.Text = error;
            return;
        }

        SetBusy(true);
        _status.Text = "Checking SFTP...";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var entries = await _remoteFiles.ListAsync(profile, "/", _password.Text, timeout.Token);
            var preview = string.Join(", ", entries.Take(5).Select(x => x.Name));
            _status.Text = $"SFTP OK. Root entries: {entries.Count}" +
                           (string.IsNullOrWhiteSpace(preview) ? string.Empty : $"\n{preview}");
        }
        catch (OperationCanceledException)
        {
            _status.Text = "SFTP test timed out or was cancelled.";
        }
        catch (Exception ex)
        {
            _status.Text = $"SFTP failed: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private bool TryCreateProfile(out ServerProfile profile, out string error)
    {
        profile = default!;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(_name.Text) || string.IsNullOrWhiteSpace(_host.Text) || string.IsNullOrWhiteSpace(_username.Text))
        {
            error = "Name, host and username are required.";
            return false;
        }

        if (!int.TryParse(_port.Text, out var port) || port is < 1 or > 65535)
        {
            error = "Port must be between 1 and 65535.";
            return false;
        }

        profile = new ServerProfile(
            _editingId ?? Guid.NewGuid(),
            _name.Text.Trim(),
            _host.Text.Trim(),
            port,
            _username.Text.Trim(),
            SshAuthenticationType.Password,
            null,
            null,
            null,
            ServerEnvironment.Development,
            Array.Empty<string>(),
            string.IsNullOrWhiteSpace(_fingerprint.Text) ? null : _fingerprint.Text.Trim());
        return true;
    }

    private void ResetEditor()
    {
        _loadingSelection = true;
        _serverPicker.SelectedIndex = -1;
        _loadingSelection = false;
        _editingId = null;
        _name.Text = string.Empty;
        _host.Text = string.Empty;
        _port.Text = "22";
        _username.Text = string.Empty;
        _password.Text = string.Empty;
        _fingerprint.Text = string.Empty;
        _rememberPassword.IsChecked = false;
        _status.Text = "New server profile.";
    }

    private void RefreshPicker(int selectedIndex = -1)
    {
        _loadingSelection = true;
        _serverPicker.ItemsSource = _profiles.Select(x => x.Name).ToArray();
        _serverPicker.SelectedIndex = selectedIndex;
        _loadingSelection = false;
    }

    private void SetBusy(bool busy)
    {
        _saveButton.IsEnabled = !busy;
        _testButton.IsEnabled = !busy;
        _sftpButton.IsEnabled = !busy;
        _newButton.IsEnabled = !busy;
        _serverPicker.IsEnabled = !busy;
    }

    private static Control Place(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }
}
