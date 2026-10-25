namespace CellAssistant.Infrastructure.Persistence.Sqlite;

using System;
using System.Threading;
using System.Threading.Tasks;

public class SqliteDatabaseInitializer : ISqliteDatabaseInitializer
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public const string DdlScript = @"
PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;

CREATE TABLE IF NOT EXISTS documents (
    id TEXT PRIMARY KEY,
    file_name TEXT NOT NULL,
    file_path TEXT NOT NULL UNIQUE,
    relative_path TEXT NOT NULL,
    document_type TEXT NOT NULL,
    folder TEXT NOT NULL,
    content_hash TEXT NOT NULL,
    equipment_id TEXT,
    manufacturer TEXT,
    criticality TEXT,
    tags_json TEXT NOT NULL DEFAULT '[]',
    chunk_count INTEGER NOT NULL DEFAULT 0,
    created_utc TEXT NOT NULL,
    modified_utc TEXT NOT NULL,
    indexed_utc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_documents_relative_path ON documents(relative_path);
CREATE INDEX IF NOT EXISTS idx_documents_equipment_id ON documents(equipment_id);

CREATE TABLE IF NOT EXISTS search_history (
    id TEXT PRIMARY KEY,
    question TEXT NOT NULL,
    answer TEXT NOT NULL,
    confidence TEXT NOT NULL,
    sources_json TEXT NOT NULL DEFAULT '[]',
    asked_utc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_search_history_asked_utc ON search_history(asked_utc DESC);

CREATE TABLE IF NOT EXISTS indexer_state (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    status TEXT NOT NULL,
    last_run_utc TEXT,
    last_error TEXT,
    reindex_requested INTEGER NOT NULL DEFAULT 0
);
";

    public SqliteDatabaseInitializer(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = DdlScript;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public void Initialize()
    {
        using var conn = _connectionFactory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = DdlScript;
        cmd.ExecuteNonQuery();
    }
}
