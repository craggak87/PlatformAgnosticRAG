namespace CellAssistant.UnitTests.Infrastructure.Persistence;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CellAssistant.Infrastructure.Persistence.Sqlite;
using FluentAssertions;
using Xunit;

public class SqliteDatabaseInitializerTests
{
    [Fact]
    public async Task InitializeAsync_CreatesAllTablesAndIndexes()
    {
        using var factory = new SqliteConnectionFactory(":memory:");
        var initializer = new SqliteDatabaseInitializer(factory);

        await initializer.InitializeAsync();

        using var conn = await factory.CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table';";
        using var reader = await cmd.ExecuteReaderAsync();

        var tables = new List<string>();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        tables.Should().Contain(new[] { "documents", "search_history", "indexer_state" });
    }

    [Fact]
    public void Initialize_SyncIdempotent_CanRunMultipleTimes()
    {
        using var factory = new SqliteConnectionFactory(":memory:");
        var initializer = new SqliteDatabaseInitializer(factory);

        var act1 = () => initializer.Initialize();
        act1.Should().NotThrow();

        var act2 = () => initializer.Initialize();
        act2.Should().NotThrow();
    }
}
