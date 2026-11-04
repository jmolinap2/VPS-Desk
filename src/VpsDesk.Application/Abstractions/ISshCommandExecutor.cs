using VpsDesk.Domain.Servers;

namespace VpsDesk.Application.Abstractions;

/// <param name="OutputReceived">
/// Opcional. Si se indica, el ejecutor entrega cada línea de stdout/stderr (sin sanear)
/// a medida que el comando la produce, en lugar de esperar a que termine.
/// Se invoca desde un hilo de fondo. El resultado final conserva la salida completa.
/// </param>
/// <param name="OutputChunkReceived">
/// Flujo incremental alternativo: fragmentos UTF-8 decodificados y señal de fin.
/// El ejecutor combina stdout/stderr en el servidor para conservar su orden.
/// El consumidor debe interpretar controles y sanear antes de mostrar el texto.
/// </param>
public sealed record SshCommandRequest(
    ServerProfile Server,
    string Command,
    TimeSpan Timeout,
    Action<string>? OutputReceived = null,
    Action<string, bool>? OutputChunkReceived = null);

public sealed record SshCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration,
    bool TimedOut,
    bool Cancelled)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut && !Cancelled;
}

public interface ISshCommandExecutor
{
    Task<SshCommandResult> ExecuteAsync(
        SshCommandRequest request,
        string? secret,
        CancellationToken cancellationToken = default);
}
