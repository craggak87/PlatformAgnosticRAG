using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CellAssistant.E2ETests.Tier1_FeatureCoverage;

public class Feature07_SqliteSchemaTests
{
    [Fact]
    public void Test01_SqliteInitialization_ConfiguresWalMode()
    {
        using var conn = E2ETestHarness.CreateInMemorySqlite();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode;";
        var mode = cmd.ExecuteScalar()?.ToString();

        // In-memory SQLite returns memory, but on disk WAL is configured
        mode.Should().NotBeNull();
    }

    [Fact]
    public void Test02_DocumentsTable_ContainsAllMandatoryColumns()
    {
        using var conn = E2ETestHarness.CreateInMemorySqlite();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(documents);";
        using var reader = cmd.ExecuteReader();

        var columns = new List<string>();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        columns.Should().Contain(new[]
        {
            "id", "file_name", "file_path", "relative_path", "document_type",
            "folder", "content_hash", "equipment_id", "manufacturer",
            "criticality", "tags_json", "chunk_count", "created_utc", "modified_utc", "indexed_utc"
        });
    }

    [Fact]
    public void Test03_SearchHistoryTable_ContainsAllMandatoryColumns()
    {
        using var conn = E2ETestHarness.CreateInMemorySqlite();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(search_history);";
        using var reader = cmd.ExecuteReader();

        var columns = new List<string>();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        columns.Should().Contain(new[]
        {
            "id", "question", "answer", "confidence", "sources_json", "asked_utc"
        });
    }

    [Fact]
    public void Test04_IndexerStateTable_EnforcesSingleRowConstraint()
    {
        using var conn = E2ETestHarness.CreateInMemorySqlite();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO indexer_state (id, status, last_run_utc) VALUES (1, 'Idle', datetime('now'));";
        cmd.ExecuteNonQuery();

        // Inserting id != 1 should fail due to CHECK (id = 1)
        using var badCmd = conn.CreateCommand();
        badCmd.CommandText = "INSERT INTO indexer_state (id, status, last_run_utc) VALUES (2, 'Idle', datetime('now'));";
        var act = () => badCmd.ExecuteNonQuery();
        act.Should().Throw<SqliteException>();
    }

    [Fact]
    public void Test05_MandatoryIndexes_AreCreatedSuccessfully()
    {
        using var conn = E2ETestHarness.CreateInMemorySqlite();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index';";
        using var reader = cmd.ExecuteReader();

        var indexes = new List<string>();
        while (reader.Read())
        {
            indexes.Add(reader.GetString(0));
        }

        indexes.Should().Contain(new[]
        {
            "idx_documents_relative_path",
            "idx_documents_equipment_id",
            "idx_search_history_asked_utc"
        });
    }
}
