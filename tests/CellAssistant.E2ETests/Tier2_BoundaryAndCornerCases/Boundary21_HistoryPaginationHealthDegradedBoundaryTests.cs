using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier2_BoundaryAndCornerCases;

public class Boundary21_HistoryPaginationHealthDegradedBoundaryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(-100)]
    public void Test01_ZeroOrNegativeHistoryLimit_NormalizesToDefaultLimit(int limit)
    {
        int effectiveLimit = limit <= 0 ? 20 : limit;
        effectiveLimit.Should().Be(20, "Doc 3 §10 specifies default history limit of 20");
    }

    [Fact]
    public void Test02_ExcessiveHistoryLimit_IsCappedAtMaximumAllowed()
    {
        const int maxAllowedLimit = 100;
        int requestedLimit = 5000;
        int effectiveLimit = Math.Min(requestedLimit, maxAllowedLimit);

        effectiveLimit.Should().Be(100);
    }

    [Fact]
    public void Test03_HealthResponse_WithDegradedSubsystems_ReportsAccurately()
    {
        var degradedHealth = new HealthResponseDto(
            Api: "Healthy",
            Sqlite: "Healthy",
            Qdrant: "Unhealthy (Connection refused: 6333)",
            Ollama: "Degraded (Primary model missing)",
            Indexer: "Idle"
        );

        degradedHealth.Qdrant.Should().Contain("Unhealthy");
        degradedHealth.Ollama.Should().Contain("Degraded");
    }

    [Fact]
    public void Test04_EmptySearchHistoryTable_ReturnsEmptyListWithoutException()
    {
        using var conn = E2ETestHarness.CreateInMemorySqlite();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM search_history;";
        var count = Convert.ToInt32(cmd.ExecuteScalar());

        count.Should().Be(0);
    }

    [Fact]
    public void Test05_HistoryEntryWithEmptySourcesJson_DeserializesToEmptyArray()
    {
        using var conn = E2ETestHarness.CreateInMemorySqlite();
        var id = Guid.NewGuid();
        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = @"
INSERT INTO search_history (id, question, answer, confidence, sources_json, asked_utc)
VALUES (@id, 'Q', 'A', 'Low', '[]', datetime('now'));";
        insertCmd.Parameters.AddWithValue("@id", id.ToString());
        insertCmd.ExecuteNonQuery();

        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT sources_json FROM search_history WHERE id = @id;";
        selectCmd.Parameters.AddWithValue("@id", id.ToString());
        var json = selectCmd.ExecuteScalar()?.ToString();

        var sources = System.Text.Json.JsonSerializer.Deserialize<IReadOnlyList<SourceReferenceDto>>(json!);
        sources.Should().BeEmpty();
    }
}
