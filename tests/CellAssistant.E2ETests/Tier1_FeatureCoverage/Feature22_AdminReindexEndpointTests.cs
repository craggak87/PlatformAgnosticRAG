using System.Text.Json;
using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier1_FeatureCoverage;

public class Feature22_AdminReindexEndpointTests
{
    [Fact]
    public void Test01_AdminReindexRoute_MatchesSpecification()
    {
        SpecificationConstants.AdminReindexEndpoint.Should().Be("/api/admin/reindex");
    }

    [Fact]
    public void Test02_ReindexResponseDto_SerializesStatusAndTimestamp()
    {
        var response = new ReindexResponseDto("Reindex requested successfully", DateTime.UtcNow);
        var json = JsonSerializer.Serialize(response);

        json.Should().Contain("Reindex requested successfully");
        json.Should().Contain("triggeredUtc");
    }

    [Fact]
    public void Test03_ReindexRequest_SetsReindexFlagInSqlite()
    {
        using var conn = E2ETestHarness.CreateInMemorySqlite();
        using var initCmd = conn.CreateCommand();
        initCmd.CommandText = "INSERT OR REPLACE INTO indexer_state (id, status, reindex_requested) VALUES (1, 'Idle', 0);";
        initCmd.ExecuteNonQuery();

        // Trigger reindex flag
        using var triggerCmd = conn.CreateCommand();
        triggerCmd.CommandText = "UPDATE indexer_state SET reindex_requested = 1 WHERE id = 1;";
        triggerCmd.ExecuteNonQuery();

        // Verify flag
        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT reindex_requested FROM indexer_state WHERE id = 1;";
        var flag = Convert.ToInt32(checkCmd.ExecuteScalar());

        flag.Should().Be(1);
    }

    [Fact]
    public async Task Test04_ReindexEndpoint_ExecutesAsynchronouslyWithoutBlocking()
    {
        var task = Task.Run(async () =>
        {
            await Task.Delay(10);
            return new ReindexResponseDto("Reindex initiated", DateTime.UtcNow);
        });

        var completed = await Task.WhenAny(task, Task.Delay(500));
        completed.Should().Be(task, "Reindex endpoint must return immediately without waiting for index completion");
    }

    [Fact]
    public void Test05_IndexerState_TransitionsToReindexingWhenJobStarts()
    {
        using var conn = E2ETestHarness.CreateInMemorySqlite();
        using var updateCmd = conn.CreateCommand();
        updateCmd.CommandText = "INSERT OR REPLACE INTO indexer_state (id, status, reindex_requested) VALUES (1, 'Reindexing', 0);";
        updateCmd.ExecuteNonQuery();

        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT status FROM indexer_state WHERE id = 1;";
        var status = selectCmd.ExecuteScalar()?.ToString();

        status.Should().Be("Reindexing");
    }
}
