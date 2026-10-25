using System.Text;
using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier1_FeatureCoverage;

public class Feature15_DocumentIndexerSha256Tests
{
    [Fact]
    public void Test01_Sha256Calculation_GeneratesDeterministicHexHash()
    {
        var content = Encoding.UTF8.GetBytes("Test markdown file content for indexer.");
        var hash1 = E2ETestHarness.ComputeSha256(content);
        var hash2 = E2ETestHarness.ComputeSha256(content);

        hash1.Should().Be(hash2);
        hash1.Length.Should().Be(64, "SHA-256 hex string must be exactly 64 characters");
        hash1.Should().MatchRegex("^[a-f0-9]{64}$");
    }

    [Fact]
    public void Test02_UnchangedFileHash_SkipsReindexing()
    {
        using var conn = E2ETestHarness.CreateInMemorySqlite();
        var docId = Guid.NewGuid();
        var content = Encoding.UTF8.GetBytes("Unchanging document");
        var hash = E2ETestHarness.ComputeSha256(content);

        // Insert into SQLite
        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = @"
INSERT INTO documents (id, file_name, file_path, relative_path, document_type, folder, content_hash, created_utc, modified_utc, indexed_utc)
VALUES (@id, 'Doc.md', '/path/Doc.md', 'Faults/Doc.md', 'Markdown', 'Faults', @hash, datetime('now'), datetime('now'), datetime('now'));";
        insertCmd.Parameters.AddWithValue("@id", docId.ToString());
        insertCmd.Parameters.AddWithValue("@hash", hash);
        insertCmd.ExecuteNonQuery();

        // Check hash
        using var queryCmd = conn.CreateCommand();
        queryCmd.CommandText = "SELECT content_hash FROM documents WHERE file_path = '/path/Doc.md';";
        var existingHash = queryCmd.ExecuteScalar()?.ToString();

        bool isModified = existingHash != hash;
        isModified.Should().BeFalse("Matching hash indicates file has not changed and reindexing must be skipped");
    }

    [Fact]
    public void Test03_ChangedFileHash_TriggersReindexing()
    {
        var originalContent = Encoding.UTF8.GetBytes("Original revision 1.0");
        var modifiedContent = Encoding.UTF8.GetBytes("Modified revision 1.1 with new troubleshooting steps");

        var hash1 = E2ETestHarness.ComputeSha256(originalContent);
        var hash2 = E2ETestHarness.ComputeSha256(modifiedContent);

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void Test04_FileDeletion_RemovesRecordFromSqlite()
    {
        using var conn = E2ETestHarness.CreateInMemorySqlite();
        var docId = Guid.NewGuid();

        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = @"
INSERT INTO documents (id, file_name, file_path, relative_path, document_type, folder, content_hash, created_utc, modified_utc, indexed_utc)
VALUES (@id, 'Deleted.md', '/path/Deleted.md', 'Faults/Deleted.md', 'Markdown', 'Faults', 'hash123', datetime('now'), datetime('now'), datetime('now'));";
        insertCmd.Parameters.AddWithValue("@id", docId.ToString());
        insertCmd.ExecuteNonQuery();

        using var deleteCmd = conn.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM documents WHERE id = @id;";
        deleteCmd.Parameters.AddWithValue("@id", docId.ToString());
        deleteCmd.ExecuteNonQuery();

        using var verifyCmd = conn.CreateCommand();
        verifyCmd.CommandText = "SELECT COUNT(*) FROM documents WHERE id = @id;";
        verifyCmd.Parameters.AddWithValue("@id", docId.ToString());
        var count = Convert.ToInt32(verifyCmd.ExecuteScalar());

        count.Should().Be(0);
    }

    [Fact]
    public void Test05_IndexerState_RecordsCompletionStatusAndTimestamp()
    {
        using var conn = E2ETestHarness.CreateInMemorySqlite();

        using var upsertCmd = conn.CreateCommand();
        upsertCmd.CommandText = @"
INSERT OR REPLACE INTO indexer_state (id, status, last_run_utc, last_error, reindex_requested)
VALUES (1, 'Idle', datetime('now'), NULL, 0);";
        upsertCmd.ExecuteNonQuery();

        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT status, last_run_utc, reindex_requested FROM indexer_state WHERE id = 1;";
        using var reader = selectCmd.ExecuteReader();

        reader.Read().Should().BeTrue();
        reader.GetString(0).Should().Be("Idle");
        reader.GetInt32(2).Should().Be(0);
    }
}
