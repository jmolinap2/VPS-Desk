using System.Formats.Tar;
using System.Globalization;
using System.Text;
using VpsDesk.Application.Abstractions;
using VpsDesk.Application.AppBackups;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Infrastructure.AppBackups;

/// <summary>
/// Base de datos: descubrir contenedores y bases, y restaurar los volcados por base
/// (pg_dump -Fc) de una copia. Las copias antiguas solo traen postgres-all.sql.gz
/// (pg_dumpall) y no se restauran desde aquí: aplicarlo sobre un servidor con datos
/// mezcla objetos y no es reversible.
/// </summary>
public sealed partial class SshAppBackupService
{
    private const string GlobalsEntry = "./db/globals.sql";

    public async Task<PostgresDiscovery> DiscoverPostgresAsync(ServerProfile server, string? container, string? secret,
        CancellationToken cancellationToken = default)
    {
        var script = $"PGC={Quote(container?.Trim() ?? string.Empty)}\n" + PostgresFunctions + """
            set -uo pipefail
            CONTAINERS=$(pg_detect)
            printf '%s\n' "$CONTAINERS" | sed '/^$/d; s/^/C /'
            [ -n "$PGC" ] || PGC=$(printf '%s\n' "$CONTAINERS" | head -n 1)
            if [ -n "$PGC" ]; then
              echo "S $PGC"
              pg_databases 2>/dev/null | sed '/^$/d; s/^/D /'
            fi
            """;
        var result = await _ssh.ExecuteAsync(new SshCommandRequest(server, Wrap(script), TimeSpan.FromSeconds(40)), secret, cancellationToken);
        if (!result.Succeeded && result.StandardOutput.Length == 0)
            throw new InvalidOperationException("No se pudo consultar PostgreSQL en el VPS. " + result.StandardError.Trim());

        var containers = new List<string>();
        var databases = new List<string>();
        string? selected = null;
        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.Length < 3 || line[1] != ' ') continue;
            var value = line[2..].Trim();
            switch (line[0])
            {
                case 'C': containers.Add(value); break;
                case 'S': selected = value; break;
                case 'D' when IsSafeDatabaseName(value): databases.Add(value); break;
            }
        }

        return new PostgresDiscovery(containers, selected, databases);
    }

    public async Task<IReadOnlyList<string>> ListBackupDatabasesAsync(ServerProfile server, AppBackupInfo backup, string? secret,
        CancellationToken cancellationToken = default)
    {
        if (!backup.StoredOnServer)
        {
            ValidateLocalBackup(server, backup);
            var names = new List<string>();
            await using var stream = File.OpenRead(backup.FullPath);
            using var reader = new TarReader(stream);
            while (await reader.GetNextEntryAsync(cancellationToken: cancellationToken) is { } entry)
            {
                if (DatabaseNameFromEntry(entry.Name) is { } name) names.Add(name);
            }
            return names.Order(StringComparer.Ordinal).ToArray();
        }

        await Task.Run(() =>
        {
            using var client = Connect(server, secret);
            ValidateRemoteBackup(client, backup);
        }, cancellationToken);
        var result = await _ssh.ExecuteAsync(new SshCommandRequest(server,
            "tar -tf " + Quote(backup.FullPath), TimeSpan.FromMinutes(5)), secret, cancellationToken);
        if (!result.Succeeded) throw new InvalidDataException("No se pudo leer el contenido de la copia en el VPS.");

        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(DatabaseNameFromEntry)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<DatabaseRestoreResult> RestoreDatabaseAsync(DatabaseRestoreRequest request, string? secret,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!request.Backup.IncludesDatabase)
            throw new InvalidOperationException("La copia seleccionada no incluye base de datos.");
        if (request.Database is { } selected && !IsSafeDatabaseName(selected))
            throw new InvalidOperationException($"El nombre de base de datos «{selected}» no es válido.");

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8];
        var work = $"/tmp/vpsdesk-restore-{stamp}";
        var source = string.Empty;

        try
        {
            if (request.Backup.StoredOnServer)
            {
                await Task.Run(() =>
                {
                    using var client = Connect(request.Server, secret);
                    ValidateRemoteBackup(client, request.Backup);
                }, cancellationToken);
                source = request.Backup.FullPath;
            }
            else
            {
                progress?.Report("Subiendo los volcados de la copia al VPS...");
                await UploadDumpsAsync(request, secret, work, cancellationToken);
            }

            var script = $"WORK={Quote(work)}\nSRC={Quote(source)}\n"
                + $"PGC={Quote(request.PostgresContainer?.Trim() ?? string.Empty)}\n"
                + $"DBSEL={Quote(request.Database ?? string.Empty)}\n"
                + PostgresFunctions
                + RestoreScript;
            var result = await _ssh.ExecuteAsync(
                new SshCommandRequest(request.Server, Wrap(script), RemoteTimeout, line => ReportRemoteLine(line, progress)),
                secret, cancellationToken);
            if (!result.Succeeded)
            {
                var reason = result.StandardOutput
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .LastOrDefault(line => line.StartsWith("ERR:", StringComparison.Ordinal));
                throw new InvalidOperationException(result.Cancelled ? "La restauración se canceló."
                    : reason is not null ? reason[4..].Trim()
                    : $"La restauración falló en el VPS (código {result.ExitCode}). {result.StandardError.Trim()}".TrimEnd());
            }

            var markers = ParseMarkers(result.StandardOutput);
            var restored = (markers.GetValueOrDefault("VPSDESK_RESTORED") ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var warnings = result.StandardOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => line.StartsWith("WARN:", StringComparison.Ordinal))
                .Select(line => line[5..].Trim())
                .ToArray();
            return new DatabaseRestoreResult(restored, warnings);
        }
        finally
        {
            await CleanupRemoteAsync(request.Server, secret, work, work + ".tar");
        }
    }

    /// <summary>
    /// Sube al VPS solo lo necesario de una copia local (roles y los volcados pedidos),
    /// leyendo el .tar en streaming sin descomprimirlo en el dispositivo.
    /// </summary>
    private Task UploadDumpsAsync(DatabaseRestoreRequest request, string? secret, string work, CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            ValidateLocalBackup(request.Server, request.Backup);
            using var client = Connect(request.Server, secret);
            client.CreateDirectory(work);
            client.ChangePermissions(work, 448); // 0700
            client.CreateDirectory(work + "/db");

            using var stream = File.OpenRead(request.Backup.FullPath);
            using var reader = new TarReader(stream);
            var uploaded = 0;
            while (reader.GetNextEntry() is { } entry)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = DatabaseNameFromEntry(entry.Name);
                var wanted = entry.Name == GlobalsEntry
                    || (name is not null && (request.Database is null || name == request.Database));
                if (!wanted || entry.DataStream is not { } data) continue;

                var target = entry.Name == GlobalsEntry ? work + "/db/globals.sql" : $"{work}/db/{name}.dump";
                client.UploadFile(data, target);
                if (name is not null) uploaded++;
            }

            if (uploaded == 0)
                throw new InvalidOperationException(request.Database is null
                    ? "La copia no trae volcados por base (formato anterior). Restáurala manualmente desde el archivo."
                    : $"La copia no contiene la base {request.Database}.");
        }, cancellationToken);

    private static string Wrap(string script)
        => $"printf %s '{Convert.ToBase64String(Encoding.UTF8.GetBytes(Unix(script)))}' | base64 -d | bash";

    // Restaura cada base en una sola transacción (si falla, la base queda como estaba).
    // De los roles solo se crean los que faltan: reaplicar globals.sql completo
    // revertiría contraseñas cambiadas después de la copia y dejaría apps sin acceso.
    private const string RestoreScript = """
        set -uo pipefail
        umask 077
        fail() { echo "ERR: $1"; rm -rf "$WORK"; exit "${2:-1}"; }

        if [ -n "$SRC" ]; then
          mkdir -p "$WORK" || fail "No se pudo crear $WORK" 30
          if [ -n "$DBSEL" ]; then
            tar -xf "$SRC" -C "$WORK" "./db/$DBSEL.dump" 2>/dev/null || fail "La copia no contiene la base $DBSEL." 31
            tar -xf "$SRC" -C "$WORK" ./db/globals.sql 2>/dev/null || true
          else
            tar -xf "$SRC" -C "$WORK" ./db 2>/dev/null \
              || fail "La copia no trae volcados por base (formato anterior). Restáurala manualmente desde el archivo." 31
          fi
        fi
        [ -d "$WORK/db" ] || fail "No se encontraron los volcados a restaurar." 31

        [ -n "$PGC" ] || PGC=$(pg_detect | head -n 1)
        [ -n "$PGC" ] || fail "No se encontró un contenedor PostgreSQL activo en el VPS." 32
        [ "$(docker inspect -f '{{.State.Running}}' "$PGC" 2>/dev/null)" = "true" ] || fail "El contenedor $PGC no está en ejecución." 32

        if [ -s "$WORK/db/globals.sql" ]; then
          EXISTING=$(pg_query "select rolname from pg_roles") || fail "No se pudieron leer los roles de $PGC." 32
          CREATED=""
          while IFS= read -r LINE; do
            ROLE=$(printf '%s\n' "$LINE" | awk '{print $3}' | sed 's/;$//; s/^"//; s/"$//')
            [ -n "$ROLE" ] || continue
            printf '%s\n' "$EXISTING" | grep -qxF -- "$ROLE" && continue
            printf '%s\n' "$LINE" | docker exec -i "$PGC" sh -c 'psql -U "${POSTGRES_USER:-postgres}" -d postgres -qX' >/dev/null 2>&1 \
              || echo "WARN: no se pudo aplicar: ${LINE%% WITH *}"
            case " $CREATED " in *" $ROLE "*) ;; *) CREATED="$CREATED $ROLE" ;; esac
          done < <(grep -E '^(CREATE|ALTER) ROLE ' "$WORK/db/globals.sql")
          [ -z "$CREATED" ] || echo "Roles creados porque faltaban:$CREATED"
        fi

        RESTORED=""
        for F in "$WORK"/db/*.dump; do
          [ -e "$F" ] || continue
          D=$(basename "$F" .dump)
          [ -z "$DBSEL" ] || [ "$D" = "$DBSEL" ] || continue
          # La base de mantenimiento «postgres» solo se restaura si se elige explícitamente.
          [ -n "$DBSEL" ] || [ "$D" != "postgres" ] || continue
          E=${D//\'/\'\'}
          if [ -z "$(pg_query "select 1 from pg_database where datname = '$E'")" ]; then
            echo "Creando la base $D..."
            docker exec "$PGC" sh -c 'createdb -U "${POSTGRES_USER:-postgres}" "$1"' _ "$D" || fail "No se pudo crear la base $D." 33
          fi
          echo "Cerrando las conexiones abiertas a $D (las aplicaciones se reconectan solas)..."
          pg_query "select count(pg_terminate_backend(pid)) from pg_stat_activity where datname = '$E' and pid <> pg_backend_pid()" >/dev/null || true
          echo "Restaurando $D en una sola transacción..."
          docker exec -i "$PGC" sh -c 'pg_restore -U "${POSTGRES_USER:-postgres}" -d "$1" --clean --if-exists --single-transaction' _ "$D" < "$F" 2>&1 | tail -n 20
          RC=${PIPESTATUS[0]}
          [ "$RC" -eq 0 ] || fail "pg_restore falló en $D (código $RC). La transacción se revirtió: la base quedó como estaba." 33
          echo "Base $D restaurada."
          RESTORED="$RESTORED $D"
        done
        [ -n "$RESTORED" ] || fail "No había bases que restaurar en la copia." 31
        rm -rf "$WORK"
        echo "VPSDESK_RESTORED=$RESTORED"
        """;
}
