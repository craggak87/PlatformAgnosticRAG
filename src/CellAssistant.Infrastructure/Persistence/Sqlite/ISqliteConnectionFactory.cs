namespace CellAssistant.Infrastructure.Persistence.Sqlite;

using Microsoft.Data.Sqlite;

public interface ISqliteConnectionFactory
{
    /// <summary>
    /// Creates and opens a new SQLite connection configured with WAL and busy timeout pragmas.
    /// </summary>
    Task<SqliteConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronously creates and opens a new SQLite connection.
    /// </summary>
    SqliteConnection CreateOpenConnection();
}
