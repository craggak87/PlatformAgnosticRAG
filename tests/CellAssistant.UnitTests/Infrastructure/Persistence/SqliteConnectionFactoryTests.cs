namespace CellAssistant.UnitTests.Infrastructure.Persistence;

using System;
using System.IO;
using System.Threading.Tasks;
using CellAssistant.Application.Options;
using CellAssistant.Infrastructure.Persistence.Sqlite;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Xunit;

public class SqliteConnectionFactoryTests
{
    [Fact]
    public void Constructor_NullOrWhitespacePath_ThrowsArgumentException()
    {
        var act1 = () => new SqliteConnectionFactory((string)null!);
        act1.Should().Throw<ArgumentException>();

        var act2 = () => new SqliteConnectionFactory("   ");
        act2.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task CreateOpenConnectionAsync_InMemory_ReturnsOpenConnectionWithPragmas()
    {
        using var factory = new SqliteConnectionFactory(":memory:");
        using var conn = await factory.CreateOpenConnectionAsync();

        conn.State.Should().Be(System.Data.ConnectionState.Open);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode;";
        var mode = cmd.ExecuteScalar()?.ToString();
        mode.Should().NotBeNull();
    }

    [Fact]
    public void CreateOpenConnection_Synchronous_ReturnsOpenConnection()
    {
        using var factory = new SqliteConnectionFactory(":memory:");
        using var conn = factory.CreateOpenConnection();

        conn.State.Should().Be(System.Data.ConnectionState.Open);
    }

    [Fact]
    public async Task CreateOpenConnectionAsync_DiskPath_CreatesDirectoryAndOpens()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CellAssistantTest_" + Guid.NewGuid().ToString("N"));
        var dbPath = Path.Combine(tempDir, "test.db");

        try
        {
            Directory.Exists(tempDir).Should().BeFalse();

            using var factory = new SqliteConnectionFactory(dbPath);
            Directory.Exists(tempDir).Should().BeTrue();

            using var conn = await factory.CreateOpenConnectionAsync();
            conn.State.Should().Be(System.Data.ConnectionState.Open);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode;";
            var mode = cmd.ExecuteScalar()?.ToString();
            mode.Should().Be("wal");
        }
        finally
        {
            // Clear pool to release file lock
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task OptionsConstructor_BindsSqlitePathCorrectly()
    {
        var options = Options.Create(new KnowledgeBaseOptions { SqlitePath = ":memory:" });
        using var factory = new SqliteConnectionFactory(options);

        using var conn = await factory.CreateOpenConnectionAsync();
        conn.State.Should().Be(System.Data.ConnectionState.Open);
    }

    [Fact]
    public void Dispose_DisposesSharedConnection()
    {
        var factory = new SqliteConnectionFactory(":memory:");
        factory.Dispose();

        var act = () => factory.CreateOpenConnection();
        act.Should().Throw<ObjectDisposedException>();
    }
}
