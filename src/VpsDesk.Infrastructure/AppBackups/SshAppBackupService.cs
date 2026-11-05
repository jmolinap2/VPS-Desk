using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Renci.SshNet;
using VpsDesk.Application.Abstractions;
using VpsDesk.Application.AppBackups;
using VpsDesk.Application.Runtime;
using VpsDesk.Domain.Servers;
using VpsDesk.Infrastructure.Ssh;

namespace VpsDesk.Infrastructure.AppBackups;

/// <summary>
/// Copia de la aplicación por SSH/SFTP. En el VPS genera un único .tar con el
/// volcado de PostgreSQL (pg_dumpall, todos los tenants), el proyecto sin .git ni
/// node_modules, la configuración del sistema y un inventario de Docker. Lo descarga,
/// verifica el SHA-256 y rota las copias locales. Nada de esto toca los contenedores.
/// </summary>
public sealed class SshAppBackupService : IAppBackupService
{
    private const string FilePattern = "*.vpsbackup.tar";
    private const int MaxKeepCopies = 20;
    private static readonly TimeSpan RemoteTimeout = TimeSpan.FromMinutes(45);

    private readonly ISshCommandExecutor _ssh;

    public SshAppBackupService(ISshCommandExecutor ssh) => _ssh = ssh;

    public string GetBackupDirectory(ServerProfile server)
        => Path.Combine(VpsDeskDataPaths.BackupsRoot, Slug(server));

    public IReadOnlyList<AppBackupInfo> List(ServerProfile server)
    {
        var directory = GetBackupDirectory(server);
        if (!Directory.Exists(directory)) return [];

        return new DirectoryInfo(directory)
            .EnumerateFiles(FilePattern)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => new AppBackupInfo(
                file.Name,
                file.FullName,
                file.Length,
                new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)))
            .ToArray();
    }

    public async Task<AppBackupResult> CreateAsync(
        AppBackupRequest request,
        string? secret,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var repository = request.RemoteRepositoryPath.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(repository))
        {
            throw new InvalidOperationException("Indica la ruta remota del proyecto: es lo que se incluye en la copia.");
        }

        var keep = Math.Clamp(request.KeepCopies, 1, MaxKeepCopies);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var remoteWork = $"/tmp/vpsdesk-backup-{stamp}";
        var remoteFile = remoteWork + ".tar";
        var directory = GetBackupDirectory(request.Server);
        var finalPath = Path.Combine(directory, $"{Slug(request.Server)}-{stamp}.vpsbackup.tar");
        var partPath = finalPath + ".part";

        Directory.CreateDirectory(directory);

        try
        {
            progress?.Report("Preparando la copia en el VPS (volcado de base de datos, proyecto y configuración)...");
            var script = BuildRemoteScript(stamp, repository, request.PostgresContainer, keep, request.KeepOnServerOnly);
            var command = $"printf %s '{Convert.ToBase64String(Encoding.UTF8.GetBytes(script))}' | base64 -d | bash";

            var result = await _ssh.ExecuteAsync(
                new SshCommandRequest(
                    request.Server,
                    command,
                    RemoteTimeout,
                    line => ReportRemoteLine(line, progress)),
                secret,
                cancellationToken);

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(DescribeRemoteFailure(result));
            }

            var markers = ParseMarkers(result.StandardOutput);
            if (!markers.TryGetValue("VPSDESK_SHA256", out var expectedSha) || expectedSha.Length != 64)
            {
                throw new InvalidOperationException("El VPS terminó la copia pero no devolvió su huella SHA-256.");
            }

            long.TryParse(markers.GetValueOrDefault("VPSDESK_SIZE"), NumberStyles.None, CultureInfo.InvariantCulture, out var expectedSize);

            if (request.KeepOnServerOnly)
            {
                // Modo ahorro de datos: nada se descarga. La copia ya está verificada en el VPS
                // (gzip -t del volcado) y rotada allí; aquí solo se informa de dónde quedó.
                var remotePath = markers.GetValueOrDefault("VPSDESK_REMOTE_PATH") ?? remoteFile;
                progress?.Report($"Copia guardada en el VPS ({FormatSize(expectedSize)}); no se descarga para ahorrar datos.");
                var remoteWarnings = result.StandardOutput
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(line => line.StartsWith("WARN:", StringComparison.Ordinal))
                    .Select(line => line[5..].Trim())
                    .ToArray();

                return new AppBackupResult(
                    new AppBackupInfo(Path.GetFileName(remotePath), remotePath, expectedSize, DateTimeOffset.UtcNow),
                    string.Equals(markers.GetValueOrDefault("VPSDESK_DB"), "yes", StringComparison.Ordinal),
                    remoteWarnings,
                    0,
                    StoredOnServer: true);
            }

            progress?.Report($"Descargando la copia ({FormatSize(expectedSize)})...");
            await DownloadAsync(request.Server, secret, remoteFile, partPath, expectedSize, progress, cancellationToken);

            progress?.Report("Verificando integridad (SHA-256)...");
            await using (var stream = new FileStream(partPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var localSha = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
                if (!string.Equals(localSha, expectedSha, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("La copia descargada no coincide con la del servidor (SHA-256 distinto). Se descartó.");
                }
            }

            File.Move(partPath, finalPath, overwrite: false);
            var info = new FileInfo(finalPath);
            var backup = new AppBackupInfo(info.Name, info.FullName, info.Length, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));

            // Solo con la copia nueva verificada se descartan las más antiguas.
            var pruned = Prune(request.Server, keep, finalPath, progress);
            var warnings = result.StandardOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => line.StartsWith("WARN:", StringComparison.Ordinal))
                .Select(line => line[5..].Trim())
                .ToArray();

            return new AppBackupResult(
                backup,
                string.Equals(markers.GetValueOrDefault("VPSDESK_DB"), "yes", StringComparison.Ordinal),
                warnings,
                pruned);
        }
        finally
        {
            TryDelete(partPath);
            await CleanupRemoteAsync(request.Server, secret, remoteWork, remoteFile);
        }
    }

    private int Prune(ServerProfile server, int keep, string newestPath, IProgress<string>? progress)
    {
        var removed = 0;
        var obsolete = List(server)
            .Where(item => !string.Equals(item.FullPath, newestPath, StringComparison.OrdinalIgnoreCase))
            .Skip(keep - 1);

        foreach (var item in obsolete)
        {
            try
            {
                File.Delete(item.FullPath);
                removed++;
                progress?.Report($"Copia antigua eliminada por rotación: {item.FileName}");
            }
            catch (IOException ex)
            {
                progress?.Report($"No se pudo eliminar {item.FileName}: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                progress?.Report($"No se pudo eliminar {item.FileName}: {ex.Message}");
            }
        }

        return removed;
    }

    private static async Task DownloadAsync(
        ServerProfile server,
        string? secret,
        string remotePath,
        string localPartPath,
        long expectedSize,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var client = new SftpClient(SshConnectionFactory.Create(server, secret, TimeSpan.FromSeconds(20)));
            SshConnectionFactory.ApplyHostKeyPolicy(client, server);
            client.Connect();

            using var output = new FileStream(localPartPath, FileMode.Create, FileAccess.Write, FileShare.None);
            var nextReport = 0L;
            client.DownloadFile(remotePath, output, downloaded =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = (long)downloaded;
                if (current < nextReport) return;

                nextReport = current + Math.Max(8L * 1024 * 1024, expectedSize / 10);
                progress?.Report(expectedSize > 0
                    ? $"Descargado {FormatSize(current)} de {FormatSize(expectedSize)}"
                    : $"Descargado {FormatSize(current)}");
            });
            client.Disconnect();
        }, cancellationToken);
    }

    private async Task CleanupRemoteAsync(ServerProfile server, string? secret, string remoteWork, string remoteFile)
    {
        try
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _ssh.ExecuteAsync(
                new SshCommandRequest(server, $"rm -rf {Quote(remoteWork)} {Quote(remoteFile)}", TimeSpan.FromSeconds(25)),
                secret,
                cleanup.Token);
        }
        catch
        {
            // La limpieza es de mejor esfuerzo: /tmp se vacía al reiniciar y no debe ocultar el resultado real.
        }
    }

    private static void ReportRemoteLine(string line, IProgress<string>? progress)
    {
        if (progress is null || string.IsNullOrWhiteSpace(line)) return;
        if (line.StartsWith("VPSDESK_", StringComparison.Ordinal)) return;
        progress.Report(line.Trim());
    }

    private static Dictionary<string, string> ParseMarkers(string output)
    {
        var markers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!raw.StartsWith("VPSDESK_", StringComparison.Ordinal)) continue;
            var separator = raw.IndexOf('=');
            if (separator > 0) markers[raw[..separator]] = raw[(separator + 1)..].Trim();
        }

        return markers;
    }

    private static string DescribeRemoteFailure(SshCommandResult result)
    {
        if (result.Cancelled) return "La copia se canceló.";
        if (result.TimedOut) return $"La copia no terminó en {RemoteTimeout.TotalMinutes:F0} minutos.";

        var reason = result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(line => line.StartsWith("ERR:", StringComparison.Ordinal));

        return reason is not null
            ? $"La copia falló en el VPS: {reason[4..].Trim()}"
            : $"La copia falló en el VPS (código {result.ExitCode}). {result.StandardError.Trim()}".TrimEnd();
    }

    private static string BuildRemoteScript(string stamp, string repository, string? postgresContainer, int keep, bool serverOnly)
        => RemoteScript
            .Replace("__STAMP__", stamp, StringComparison.Ordinal)
            .Replace("__KEEP__", keep.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("__SERVER_ONLY__", serverOnly ? "1" : "0", StringComparison.Ordinal)
            .Replace("__REPO__", Quote(repository), StringComparison.Ordinal)
            .Replace("__PG__", Quote(postgresContainer?.Trim() ?? string.Empty), StringComparison.Ordinal);

    private static string Quote(string value)
        => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    private static string Slug(ServerProfile server)
    {
        var chars = server.Name
            .Select(c => char.IsLetterOrDigit(c) && c < 128 ? char.ToLowerInvariant(c) : '-')
            .ToArray();
        var slug = new string(chars).Trim('-');
        return slug.Length == 0 ? server.Id.ToString("N")[..8] : slug;
    }

    private static string FormatSize(long bytes)
        => bytes switch
        {
            <= 0 => "tamaño desconocido",
            < 1024 * 1024 => $"{bytes / 1024.0:F0} KB",
            < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:F1} MB",
            _ => $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB"
        };

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // Se envía en base64 para no depender del shell de login ni de escapes.
    private const string RemoteScript = """
        set -uo pipefail
        umask 077
        STAMP=__STAMP__
        REPO=__REPO__
        PGC=__PG__
        KEEP=__KEEP__
        SERVER_ONLY=__SERVER_ONLY__
        WORK="/tmp/vpsdesk-backup-$STAMP"
        OUT="$WORK.tar"
        fail() { echo "ERR: $1"; rm -rf "$WORK" "$OUT"; exit "${2:-1}"; }

        [ -d "$REPO" ] || fail "El proyecto no existe en el VPS: $REPO" 11
        AVAIL=$(df -Pk /tmp | awk 'NR==2{print $4}')
        [ "${AVAIL:-0}" -ge 512000 ] || fail "Menos de 500 MB libres en /tmp; libera espacio antes de copiar." 12
        mkdir -p "$WORK/content" || fail "No se pudo crear $WORK" 10

        {
          echo "created_utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
          echo "host=$(hostname)"
          echo "repo=$REPO"
          . /etc/os-release 2>/dev/null && echo "os=$PRETTY_NAME"
          echo "--- docker ps -a ---"
          docker ps -a --format '{{.Names}} | {{.Image}} | {{.Status}}' 2>&1
          echo "--- docker volumes ---"
          docker volume ls --format '{{.Name}}' 2>&1
        } > "$WORK/content/manifest.txt"

        DB=no
        if [ -z "$PGC" ]; then
          PGC=$(docker ps --format '{{.Names}} {{.Image}}' 2>/dev/null | awk 'tolower($2) ~ /postgres/ {print $1; exit}')
        fi
        if [ -n "$PGC" ]; then
          echo "Volcando PostgreSQL (contenedor $PGC, todas las bases)..."
          docker exec "$PGC" sh -c 'pg_dumpall -U "${POSTGRES_USER:-postgres}"' | gzip -c > "$WORK/content/postgres-all.sql.gz" \
            || fail "pg_dumpall falló en el contenedor $PGC; no se genera una copia sin base de datos." 20
          gzip -t "$WORK/content/postgres-all.sql.gz" || fail "El volcado de PostgreSQL quedó corrupto." 20
          [ "$(stat -c %s "$WORK/content/postgres-all.sql.gz")" -gt 200 ] || fail "El volcado de PostgreSQL salió vacío." 20
          DB=yes
        else
          echo "WARN: no se encontró un contenedor PostgreSQL; la copia NO incluye base de datos."
        fi

        echo "Empaquetando el proyecto (sin .git ni node_modules)..."
        tar -czf "$WORK/content/project.tar.gz" --exclude='.git' --exclude='node_modules' -C "$(dirname "$REPO")" "$(basename "$REPO")" 2>/dev/null
        RC=$?
        [ "$RC" -le 1 ] || fail "No se pudo empaquetar el proyecto (tar código $RC)." 30

        CFG=""
        for P in /etc/nginx /etc/docker /etc/fail2ban /etc/ufw /etc/ssh/sshd_config; do
          [ -e "$P" ] && CFG="$CFG $P"
        done
        if [ -n "$CFG" ]; then
          echo "Empaquetando la configuración del sistema..."
          tar -czf "$WORK/content/system-config.tar.gz" $CFG 2>/dev/null
          [ $? -le 1 ] || echo "WARN: parte de la configuración del sistema no se pudo leer (¿falta permiso de root?)."
        fi

        tar -cf "$OUT" -C "$WORK/content" . || fail "No se pudo crear el paquete final." 40
        rm -rf "$WORK"
        if [ "$SERVER_ONLY" = "1" ]; then
          DEST="${HOME:-/root}/vpsdesk-backups"
          mkdir -p "$DEST" || fail "No se pudo crear $DEST" 50
          FINAL="$DEST/vpsdesk-$STAMP.vpsbackup.tar"
          mv "$OUT" "$FINAL" || fail "No se pudo guardar la copia en $DEST" 50
          OUT="$FINAL"
          ls -1t "$DEST"/*.vpsbackup.tar 2>/dev/null | tail -n +$((KEEP + 1)) | xargs -r rm -f
          echo "VPSDESK_REMOTE_PATH=$OUT"
        fi
        echo "VPSDESK_SHA256=$(sha256sum "$OUT" | cut -d' ' -f1)"
        echo "VPSDESK_SIZE=$(stat -c %s "$OUT")"
        echo "VPSDESK_DB=$DB"
        """;
}
