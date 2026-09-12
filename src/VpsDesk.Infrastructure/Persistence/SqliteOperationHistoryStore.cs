using Microsoft.Data.Sqlite;
using VpsDesk.Application.Activity;
using VpsDesk.Domain.Activity;

namespace VpsDesk.Infrastructure.Persistence;

public sealed class SqliteOperationHistoryStore : IOperationHistoryStore
{
    private readonly string _connectionString;

    public SqliteOperationHistoryStore(string? databasePath = null)
    {
        var path = databasePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VPSDesk",
            "vpsdesk.db");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        EnsureSchema();
    }

    public async Task AddAsync(OperationHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO operation_history (
                id, server_id, server_name, environment, kind, action, summary, outcome,
                started_at, finished_at, remote_repository_path, branch, previous_commit,
                deployed_commit, compose_file, failed_step, output, details)
            VALUES (
                $id, $serverId, $serverName, $environment, $kind, $action, $summary, $outcome,
                $startedAt, $finishedAt, $remoteRepositoryPath, $branch, $previousCommit,
                $deployedCommit, $composeFile, $failedStep, $output, $details);
            """;

        command.Parameters.AddWithValue("$id", entry.Id.ToString());
        command.Parameters.AddWithValue("$serverId", entry.ServerId.ToString());
        command.Parameters.AddWithValue("$serverName", entry.ServerName);
        command.Parameters.AddWithValue("$environment", entry.Environment);
        command.Parameters.AddWithValue("$kind", entry.Kind.ToString());
        command.Parameters.AddWithValue("$action", entry.Action);
        command.Parameters.AddWithValue("$summary", entry.Summary);
        command.Parameters.AddWithValue("$outcome", entry.Outcome.ToString());
        command.Parameters.AddWithValue("$startedAt", entry.StartedAt.ToString("O"));
        command.Parameters.AddWithValue("$finishedAt", entry.FinishedAt.ToString("O"));
        command.Parameters.AddWithValue("$remoteRepositoryPath", (object?)entry.RemoteRepositoryPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$branch", (object?)entry.Branch ?? DBNull.Value);
        command.Parameters.AddWithValue("$previousCommit", (object?)entry.PreviousCommit ?? DBNull.Value);
        command.Parameters.AddWithValue("$deployedCommit", (object?)entry.DeployedCommit ?? DBNull.Value);
        command.Parameters.AddWithValue("$composeFile", (object?)entry.ComposeFile ?? DBNull.Value);
        command.Parameters.AddWithValue("$failedStep", (object?)entry.FailedStep ?? DBNull.Value);
        command.Parameters.AddWithValue("$output", (object?)entry.Output ?? DBNull.Value);
        command.Parameters.AddWithValue("$details", (object?)entry.Details ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task<IReadOnlyList<OperationHistoryEntry>> GetRecentAsync(int limit = 20, CancellationToken cancellationToken = default)
        => QueryAsync(null, null, limit, cancellationToken);

    public Task<IReadOnlyList<OperationHistoryEntry>> GetDeploymentsAsync(Guid? serverId = null, int limit = 100, CancellationToken cancellationToken = default)
        => QueryAsync(OperationKind.Deployment, serverId, limit, cancellationToken);

    private async Task<IReadOnlyList<OperationHistoryEntry>> QueryAsync(
        OperationKind? kind,
        Guid? serverId,
        int limit,
        CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit, 1, 500);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        var where = new List<string>();
        if (kind is not null)
        {
            where.Add("kind = $kind");
            command.Parameters.AddWithValue("$kind", kind.Value.ToString());
        }
        if (serverId is not null)
        {
            where.Add("server_id = $serverId");
            command.Parameters.AddWithValue("$serverId", serverId.Value.ToString());
        }

        command.CommandText = $"""
            SELECT id, server_id, server_name, environment, kind, action, summary, outcome,
                   started_at, finished_at, remote_repository_path, branch, previous_commit,
                   deployed_commit, compose_file, failed_step, output, details
            FROM operation_history
            {(where.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", where))}
            ORDER BY started_at DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        var result = new List<OperationHistoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new OperationHistoryEntry(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                Enum.Parse<OperationKind>(reader.GetString(4)),
                reader.GetString(5),
                reader.GetString(6),
                Enum.Parse<OperationOutcome>(reader.GetString(7)),
                DateTimeOffset.Parse(reader.GetString(8)),
                DateTimeOffset.Parse(reader.GetString(9)),
                ReadNullable(reader, 10),
                ReadNullable(reader, 11),
                ReadNullable(reader, 12),
                ReadNullable(reader, 13),
                ReadNullable(reader, 14),
                ReadNullable(reader, 15),
                ReadNullable(reader, 16),
                ReadNullable(reader, 17)));
        }

        return result;
    }

    private void EnsureSchema()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS operation_history (
                id TEXT PRIMARY KEY,
                server_id TEXT NOT NULL,
                server_name TEXT NOT NULL,
                environment TEXT NOT NULL,
                kind TEXT NOT NULL,
                action TEXT NOT NULL,
                summary TEXT NOT NULL,
                outcome TEXT NOT NULL,
                started_at TEXT NOT NULL,
                finished_at TEXT NOT NULL,
                remote_repository_path TEXT NULL,
                branch TEXT NULL,
                previous_commit TEXT NULL,
                deployed_commit TEXT NULL,
                compose_file TEXT NULL,
                failed_step TEXT NULL,
                output TEXT NULL,
                details TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_operation_history_started_at
                ON operation_history(started_at DESC);
            CREATE INDEX IF NOT EXISTS ix_operation_history_server_kind
                ON operation_history(server_id, kind, started_at DESC);
            """;
        command.ExecuteNonQuery();
    }

    private static string? ReadNullable(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
