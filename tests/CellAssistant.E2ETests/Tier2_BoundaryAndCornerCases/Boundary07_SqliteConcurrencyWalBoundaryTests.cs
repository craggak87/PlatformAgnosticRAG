using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CellAssistant.E2ETests.Tier2_BoundaryAndCornerCases;

public class Boundary07_SqliteConcurrencyWalBoundaryTests
{
    [Fact]
    public void Test01_SqliteUniqueFilePathConstraint_ThrowsOnDuplicate()
    {
        using var conn = E2ETestHarness.CreateInMemorySqlite();
        var path = "/data/KnowledgeBase/Faults/Unique.md";

        using var cmd1 = conn.CreateCommand();
        cmd1.CommandText = @"
INSERT INTO documents (id, file_name, file_path, relative_path, document_type, folder, content_hash, created_utc, modified_utc, indexed_utc)
VALUES ('11111111-1111-1111-1111-111111111111', 'Unique.md', @p, 'Faults/Unique.md', 'Markdown', 'Faults', 'h1', datetime('now'), datetime('now'), datetime('now'));";
        cmd1.Parameters.AddWithValue("@p", path);
        cmd1.ExecuteNonQuery();

        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = @"
INSERT INTO documents (id, file_name, file_path, relative_path, document_type, folder, content_hash, created_utc, modified_utc, indexed_utc)
VALUES ('22222222-2222-2222-2222-222222222222', 'Unique.md', @p, 'Faults/Unique.md', 'Markdown', 'Faults', 'h2', datetime('now'), datetime('now'), datetime('now'));";
        cmd2.Parameters.AddWithValue("@p", path);

        var act = () => cmd2.ExecuteNonQuery();
        act.Should().Throw<SqliteException>("file_path column has UNIQUE constraint");
    }

    [Fact]
    public void Test02_TransactionRollback_PreservesPriorStateOnFailure()
    {
        using var conn = E2ETestHarness.CreateInMemorySqlite();
        using var initCmd = conn.CreateCommand();
        initCmd.CommandText = "SELECT COUNT(*) FROM documents;";
        var initialCount = Convert.ToInt32(initCmd.ExecuteScalar());

        using var trans = conn.BeginTransaction();
        try
        {
            using var insertCmd = conn.CreateCommand();
            insertCmd.Transaction = trans;
            insertCmd.CommandText = @"
INSERT INTO documents (id, file_name, file_path, relative_path, document_type, folder, content_hash, created_utc, modified_utc, indexed_utc)
VALUES ('33333333-3333-3333-3333-333333333333', 'Roll.md', '/p/Roll.md', 'Roll.md', 'Markdown', 'Faults', 'h', datetime('now'), datetime('now'), datetime('now'));";
            insertCmd.ExecuteNonQuery();

            // Simulate explicit failure and rollback
            trans.Rollback();
        }
        catch
        {
            trans.Rollback();
        }

        using var verifyCmd = conn.CreateCommand();
        verifyCmd.CommandText = "SELECT COUNT(*) FROM documents;";
        var postRollbackCount = Convert.ToInt32(verifyCmd.ExecuteScalar());

        postRollbackCount.Should().Be(initialCount);
    }

    [Fact]
    public async Task Test03_ConcurrentReads_ExecuteWithoutDeadlocks()
    {
        using var conn = E2ETestHarness.CreateInMemorySqlite();
        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM search_history;";
            return Convert.ToInt32(cmd.ExecuteScalar());
        })).ToArray();

        var results = await Task.WhenAll(tasks);
        foreach (var r in results)
        {
            r.Should().Be(0);
        }
    }

    [Fact]
    public void Test04_TagsJsonDefault_IsEmptyJsonArray()
    {
        using var conn = E2ETestHarness.CreateInMemorySqlite();
        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = @"
INSERT INTO documents (id, file_name, file_path, relative_path, document_type, folder, content_hash, created_utc, modified_utc, indexed_utc)
VALUES ('44444444-4444-4444-4444-444444444444', 'DefaultTags.md', '/p/dt.md', 'dt.md', 'Markdown', 'Faults', 'h', datetime('now'), datetime('now'), datetime('now'));";
        insertCmd.ExecuteNonQuery();

        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT tags_json FROM documents WHERE id = '44444444-4444-4444-4444-444444444444';";
        var tagsJson = selectCmd.ExecuteScalar()?.ToString();

        tagsJson.Should().Be("[]");
    }

    [Fact]
    public void Test05_NullEquipmentId_StoredAsNullInSqlite()
    {
        using var conn = E2ETestHarness.CreateInMemorySqlite();
        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = @"
INSERT INTO documents (id, file_name, file_path, relative_path, document_type, folder, content_hash, equipment_id, created_utc, modified_utc, indexed_utc)
VALUES ('55555555-5555-5555-5555-555555555555', 'NoEq.md', '/p/ne.md', 'ne.md', 'Markdown', 'Faults', 'h', NULL, datetime('now'), datetime('now'), datetime('now'));";
        insertCmd.ExecuteNonQuery();

        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT equipment_id FROM documents WHERE id = '55555555-5555-5555-5555-555555555555';";
        var val = selectCmd.ExecuteScalar();

        val.Should().Be(DBNull.Value);
    }
}
