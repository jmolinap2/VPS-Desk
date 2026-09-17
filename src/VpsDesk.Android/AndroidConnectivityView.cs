using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using VpsDesk.Application.Abstractions;
using VpsDesk.Domain.Servers;
using VpsDesk.Infrastructure.Ssh;

namespace VpsDesk.Android;

public sealed class AndroidConnectivityView : UserControl
{
    private readonly TextBox _host = new() { Watermark = "Host or IP" };
    private readonly TextBox _port = new() { Text = "22", Watermark = "Port" };
    private readonly TextBox _username = new() { Watermark = "Username" };
    private readonly TextBox _password = new() { Watermark = "Password", PasswordChar = '●' };
    private readonly TextBox _fingerprint = new() { Watermark = "Optional SHA256 host fingerprint" };
    private readonly TextBlock _status = new() { Text = "Android SSH compatibility spike ready." };
    private readonly Button _testButton = new() { Content = "Test SSH", HorizontalAlignment = HorizontalAlignment.Stretch };

    public AndroidConnectivityView()
    {
        _testButton.Click += async (_, _) => await TestSshAsync();

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "VPS Desk Android", FontSize = 26 },
                    new TextBlock
                    {
                        Text = "Foundation diagnostic. Uses the same SSH executor as the desktop app.",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    _host,
                    _port,
                    _username,
                    _password,
                    _fingerprint,
                    _testButton,
                    _status
                }
            }
        };
    }

    private async Task TestSshAsync()
    {
        if (string.IsNullOrWhiteSpace(_host.Text) ||
            string.IsNullOrWhiteSpace(_username.Text) ||
            !int.TryParse(_port.Text, out var port) || port is < 1 or > 65535)
        {
            _status.Text = "Host, username and a valid port are required.";
            return;
        }

        _testButton.IsEnabled = false;
        _status.Text = "Connecting...";

        try
        {
            var profile = new ServerProfile(
                Guid.NewGuid(),
                "Android diagnostic",
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

            ISshCommandExecutor executor = new SshNetCommandExecutor();
            var result = await executor.ExecuteAsync(
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
            _testButton.IsEnabled = true;
        }
    }
}
