using VpsDesk.Domain.Servers;

namespace VpsDesk.Application.Abstractions;

public sealed record TerminalSessionOptions(
    uint Columns = 120,
    uint Rows = 36,
    string TerminalName = "xterm-256color");

public sealed class TerminalOutputEventArgs(string text) : EventArgs
{
    public string Text { get; } = text;
}

public interface IInteractiveTerminalSession : IAsyncDisposable
{
    bool IsConnected { get; }
    event EventHandler<TerminalOutputEventArgs>? OutputReceived;

    Task WriteAsync(string text, CancellationToken cancellationToken = default);
    Task ResizeAsync(uint columns, uint rows, CancellationToken cancellationToken = default);
}

public interface IInteractiveTerminalService
{
    Task<IInteractiveTerminalSession> ConnectAsync(
        ServerProfile server,
        string? secret,
        TerminalSessionOptions options,
        CancellationToken cancellationToken = default);
}
