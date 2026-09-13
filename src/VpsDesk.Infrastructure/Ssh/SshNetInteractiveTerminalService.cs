using System.Text;
using Renci.SshNet;
using VpsDesk.Application.Abstractions;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Infrastructure.Ssh;

public sealed class SshNetInteractiveTerminalService : IInteractiveTerminalService
{
    public async Task<IInteractiveTerminalSession> ConnectAsync(
        ServerProfile server,
        string? secret,
        TerminalSessionOptions options,
        CancellationToken cancellationToken = default)
    {
        var client = new SshClient(SshConnectionFactory.Create(server, secret, TimeSpan.FromSeconds(15)));
        SshConnectionFactory.ApplyHostKeyPolicy(client, server);

        try
        {
            await Task.Run(client.Connect, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var shell = client.CreateShellStream(
                options.TerminalName,
                options.Columns,
                options.Rows,
                Math.Max(options.Columns * 8, 800),
                Math.Max(options.Rows * 18, 600),
                16 * 1024);

            return new SshNetInteractiveTerminalSession(client, shell);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private sealed class SshNetInteractiveTerminalSession : IInteractiveTerminalSession
    {
        private readonly SshClient _client;
        private readonly ShellStream _shell;
        private readonly CancellationTokenSource _lifetime = new();
        private readonly Task _readerTask;
        private bool _disposed;

        public bool IsConnected => !_disposed && _client.IsConnected;
        public event EventHandler<TerminalOutputEventArgs>? OutputReceived;

        public SshNetInteractiveTerminalSession(SshClient client, ShellStream shell)
        {
            _client = client;
            _shell = shell;
            _readerTask = Task.Run(ReadLoopAsync);
        }

        public async Task WriteAsync(string text, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_client.IsConnected) throw new InvalidOperationException("SSH terminal session is not connected.");

            var bytes = Encoding.UTF8.GetBytes(text);
            await _shell.WriteAsync(bytes.AsMemory(), cancellationToken);
            await _shell.FlushAsync(cancellationToken);
        }

        public Task ResizeAsync(uint columns, uint rows, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            _shell.SendWindowChangeRequest(columns, rows, Math.Max(columns * 8, 800), Math.Max(rows * 18, 600));
            return Task.CompletedTask;
        }

        private async Task ReadLoopAsync()
        {
            var buffer = new byte[8192];
            var decoder = Encoding.UTF8.GetDecoder();
            var chars = new char[Encoding.UTF8.GetMaxCharCount(buffer.Length)];

            try
            {
                while (!_lifetime.IsCancellationRequested && _client.IsConnected)
                {
                    var read = await _shell.ReadAsync(buffer.AsMemory(), _lifetime.Token);
                    if (read <= 0) break;

                    decoder.Convert(
                        buffer,
                        0,
                        read,
                        chars,
                        0,
                        chars.Length,
                        flush: false,
                        out _,
                        out var charsUsed,
                        out _);

                    if (charsUsed > 0)
                    {
                        OutputReceived?.Invoke(this, new TerminalOutputEventArgs(new string(chars, 0, charsUsed)));
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                if (!_disposed)
                {
                    OutputReceived?.Invoke(this, new TerminalOutputEventArgs($"\r\n[terminal disconnected: {ex.Message}]\r\n"));
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();

            try { _shell.Dispose(); } catch { }
            try
            {
                if (_client.IsConnected) _client.Disconnect();
            }
            catch { }
            _client.Dispose();

            try { await _readerTask; } catch { }
            _lifetime.Dispose();
        }
    }
}
