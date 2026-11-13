namespace VpsDesk.Application.Abstractions;

/// <summary>Solo para consultas sin efectos secundarios: cada intento abre otra sesión SSH.</summary>
public static class SshReadRetry
{
    public static async Task<SshCommandResult> ExecuteAsync(
        ISshCommandExecutor ssh, SshCommandRequest request, string? secret,
        CancellationToken cancellationToken = default, Action<int>? retrying = null)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ssh.ExecuteAsync(request, secret, cancellationToken);
            if (!result.ConnectionLost || result.Cancelled || result.TimedOut || attempt >= 3)
                return result with { Duration = started.Elapsed };
            retrying?.Invoke(attempt + 1);
            await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
        }
    }
}
