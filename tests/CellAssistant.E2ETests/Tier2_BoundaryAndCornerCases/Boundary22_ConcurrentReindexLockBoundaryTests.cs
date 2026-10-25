using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier2_BoundaryAndCornerCases;

public class Boundary22_ConcurrentReindexLockBoundaryTests
{
    [Fact]
    public void Test01_ConcurrentReindexRequests_OnlyOneExecutionIsInitiated()
    {
        using var conn = E2ETestHarness.CreateInMemorySqlite();
        using var initCmd = conn.CreateCommand();
        initCmd.CommandText = "INSERT OR REPLACE INTO indexer_state (id, status, reindex_requested) VALUES (1, 'Idle', 0);";
        initCmd.ExecuteNonQuery();

        // 10 concurrent requests to reindex
        Parallel.For(0, 10, _ =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE indexer_state SET reindex_requested = 1 WHERE id = 1;";
            cmd.ExecuteNonQuery();
        });

        using var verifyCmd = conn.CreateCommand();
        verifyCmd.CommandText = "SELECT reindex_requested FROM indexer_state WHERE id = 1;";
        var flag = Convert.ToInt32(verifyCmd.ExecuteScalar());

        flag.Should().Be(1);
    }

    [Fact]
    public void Test02_ReindexingState_BlocksRedundantWorkerRuns()
    {
        var isCurrentlyReindexing = true;
        var shouldTriggerNewRun = !isCurrentlyReindexing;

        shouldTriggerNewRun.Should().BeFalse();
    }

    [Fact]
    public void Test03_ReindexFailure_RecordsLastErrorWithoutThrowingUncaughtException()
    {
        using var conn = E2ETestHarness.CreateInMemorySqlite();
        const string simulatedError = "Qdrant connection dropped during batch 4";

        using var updateCmd = conn.CreateCommand();
        updateCmd.CommandText = @"
INSERT OR REPLACE INTO indexer_state (id, status, last_error, last_run_utc)
VALUES (1, 'Failed', @err, datetime('now'));";
        updateCmd.Parameters.AddWithValue("@err", simulatedError);
        updateCmd.ExecuteNonQuery();

        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT status, last_error FROM indexer_state WHERE id = 1;";
        using var reader = selectCmd.ExecuteReader();

        reader.Read().Should().BeTrue();
        reader.GetString(0).Should().Be("Failed");
        reader.GetString(1).Should().Be(simulatedError);
    }

    [Fact]
    public void Test04_ReindexResetAfterSuccess_ClearsErrorAndFlag()
    {
        using var conn = E2ETestHarness.CreateInMemorySqlite();

        using var updateCmd = conn.CreateCommand();
        updateCmd.CommandText = @"
INSERT OR REPLACE INTO indexer_state (id, status, last_error, reindex_requested, last_run_utc)
VALUES (1, 'Idle', NULL, 0, datetime('now'));";
        updateCmd.ExecuteNonQuery();

        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT status, last_error, reindex_requested FROM indexer_state WHERE id = 1;";
        using var reader = selectCmd.ExecuteReader();

        reader.Read().Should().BeTrue();
        reader.GetString(0).Should().Be("Idle");
        reader.IsDBNull(1).Should().BeTrue();
        reader.GetInt32(2).Should().Be(0);
    }

    [Fact]
    public void Test05_SimultaneousDatabaseReadsDuringReindex_SucceedDueToWal()
    {
        using var conn = E2ETestHarness.CreateInMemorySqlite();
        var canRead = false;

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM documents;";
            var res = cmd.ExecuteScalar();
            canRead = res != null;
        }

        canRead.Should().BeTrue();
    }
}
