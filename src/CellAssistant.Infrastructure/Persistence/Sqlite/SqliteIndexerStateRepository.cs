namespace CellAssistant.Infrastructure.Persistence.Sqlite;

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Application.Ports;
using CellAssistant.Domain.Entities;
using CellAssistant.Domain.Enums;
using Microsoft.Data.Sqlite;

public class SqliteIndexerStateRepository : IIndexerStateRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SqliteIndexerStateRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<IndexerState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
SELECT id, status, last_run_utc, last_error, reindex_requested
FROM indexer_state
WHERE id = 1;
";

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new IndexerState
            {
                Id = 1,
                Status = Enum.TryParse<IndexerStatus>(reader.GetString(1), out var s) ? s : IndexerStatus.Idle,
                LastRunUtc = reader.IsDBNull(2) ? null : ParseUtc(reader.GetString(2)),
                LastError = reader.IsDBNull(3) ? null : reader.GetString(3),
                ReindexRequested = reader.GetInt32(4) != 0
            };
        }

        return new IndexerState
        {
            Id = 1,
            Status = IndexerStatus.Idle,
            ReindexRequested = false
        };
    }

    public async Task UpdateStateAsync(IndexerState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
INSERT INTO indexer_state (id, status, last_run_utc, last_error, reindex_requested)
VALUES (1, @status, @last_run_utc, @last_error, @reindex_requested)
ON CONFLICT(id) DO UPDATE SET
    status = excluded.status,
    last_run_utc = excluded.last_run_utc,
    last_error = excluded.last_error,
    reindex_requested = excluded.reindex_requested;
";

        cmd.Parameters.AddWithValue("@status", state.Status.ToString());
        cmd.Parameters.AddWithValue("@last_run_utc", state.LastRunUtc.HasValue ? state.LastRunUtc.Value.ToUniversalTime().ToString("O") : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@last_error", (object?)state.LastError ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@reindex_requested", state.ReindexRequested ? 1 : 0);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateStatusAsync(IndexerStatus status, string? lastError = null, CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
INSERT INTO indexer_state (id, status, last_error, reindex_requested)
VALUES (1, @status, @last_error, 0)
ON CONFLICT(id) DO UPDATE SET
    status = excluded.status,
    last_error = excluded.last_error;
";

        cmd.Parameters.AddWithValue("@status", status.ToString());
        cmd.Parameters.AddWithValue("@last_error", (object?)lastError ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetReindexRequestedAsync(bool requested, CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
INSERT INTO indexer_state (id, status, reindex_requested)
VALUES (1, 'Idle', @reindex_requested)
ON CONFLICT(id) DO UPDATE SET
    reindex_requested = excluded.reindex_requested;
";

        cmd.Parameters.AddWithValue("@reindex_requested", requested ? 1 : 0);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DateTimeOffset ParseUtc(string val)
    {
        if (DateTimeOffset.TryParse(val, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
        {
            return dto;
        }
        return DateTimeOffset.UtcNow;
    }
}
