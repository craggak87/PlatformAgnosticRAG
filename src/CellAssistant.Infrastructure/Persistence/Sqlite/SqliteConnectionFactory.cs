namespace CellAssistant.Infrastructure.Persistence.Sqlite;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Application.Options;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

public class SqliteConnectionFactory : ISqliteConnectionFactory, IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection? _sharedConnection;
    private bool _disposed;

    public SqliteConnectionFactory(IOptions<KnowledgeBaseOptions> options)
        : this(options?.Value?.SqlitePath ?? "./data/metadata.db")
    {
    }

    public SqliteConnectionFactory(string sqlitePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlitePath);

        if (sqlitePath.Contains('=') || sqlitePath.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            if (sqlitePath.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
            {
                _connectionString = $"Data Source=mem_{Guid.NewGuid():N};Mode=Memory;Cache=Shared;";
            }
            else
            {
                _connectionString = sqlitePath;
            }

            if (_connectionString.Contains("Mode=Memory", StringComparison.OrdinalIgnoreCase))
            {
                if (!_connectionString.Contains("Cache=Shared", StringComparison.OrdinalIgnoreCase))
                {
                    _connectionString = _connectionString.TrimEnd(';') + ";Cache=Shared;";
                }
                _sharedConnection = new SqliteConnection(_connectionString);
                _sharedConnection.Open();
                ApplyPragmas(_sharedConnection);
            }
        }
        else
        {
            var fullPath = Path.GetFullPath(sqlitePath);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = fullPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
                DefaultTimeout = 5
            };
            _connectionString = builder.ToString();
        }
    }

    public async Task<SqliteConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await ApplyPragmasAsync(conn, cancellationToken);
        return conn;
    }

    public SqliteConnection CreateOpenConnection()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        ApplyPragmas(conn);
        return conn;
    }

    private static void ApplyPragmas(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
        cmd.ExecuteNonQuery();
    }

    private static async Task ApplyPragmasAsync(SqliteConnection conn, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sharedConnection?.Dispose();
    }
}
