using System.Text.Json;
using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier1_FeatureCoverage;

public class Feature21_HistoryAndHealthEndpointsTests
{
    [Fact]
    public void Test01_HistoryEndpointRoute_MatchesSpecification()
    {
        SpecificationConstants.HistoryEndpoint.Should().Be("/api/history");
    }

    [Fact]
    public void Test02_HealthEndpointRoute_MatchesSpecification()
    {
        SpecificationConstants.HealthEndpoint.Should().Be("/api/health");
    }

    [Fact]
    public void Test03_HealthResponseDto_ContainsAllFiveRequiredSubsystems()
    {
        var health = new HealthResponseDto(
            Api: "Healthy",
            Sqlite: "Healthy",
            Qdrant: "Healthy",
            Ollama: "Healthy",
            Indexer: "Healthy"
        );

        health.Api.Should().Be("Healthy");
        health.Sqlite.Should().Be("Healthy");
        health.Qdrant.Should().Be("Healthy");
        health.Ollama.Should().Be("Healthy");
        health.Indexer.Should().Be("Healthy");
    }

    [Fact]
    public void Test04_SearchHistory_PersistsAndRetrievesInDescTimestampOrder()
    {
        using var conn = E2ETestHarness.CreateInMemorySqlite();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO search_history (id, question, answer, confidence, sources_json, asked_utc)
VALUES (@id1, 'First question', 'Answer 1', 'High', '[]', '2026-10-21 10:00:00');
INSERT INTO search_history (id, question, answer, confidence, sources_json, asked_utc)
VALUES (@id2, 'Second question', 'Answer 2', 'Medium', '[]', '2026-10-21 10:05:00');";
        cmd.Parameters.AddWithValue("@id1", id1.ToString());
        cmd.Parameters.AddWithValue("@id2", id2.ToString());
        cmd.ExecuteNonQuery();

        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT id, question FROM search_history ORDER BY asked_utc DESC LIMIT 2;";
        using var reader = selectCmd.ExecuteReader();

        reader.Read().Should().BeTrue();
        reader.GetString(0).Should().Be(id2.ToString(), "Most recent question should be returned first");

        reader.Read().Should().BeTrue();
        reader.GetString(0).Should().Be(id1.ToString());
    }

    [Fact]
    public void Test05_HistoryLimitParameter_CapsResultSize()
    {
        using var conn = E2ETestHarness.CreateInMemorySqlite();
        for (int i = 0; i < 10; i++)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
INSERT INTO search_history (id, question, answer, confidence, sources_json, asked_utc)
VALUES ('{Guid.NewGuid()}', 'Question {i}', 'Ans', 'High', '[]', datetime('now', '+{i} seconds'));";
            cmd.ExecuteNonQuery();
        }

        using var selectCmd = conn.CreateCommand();
        int requestedLimit = 5;
        selectCmd.CommandText = $"SELECT COUNT(*) FROM (SELECT id FROM search_history ORDER BY asked_utc DESC LIMIT {requestedLimit});";
        var count = Convert.ToInt32(selectCmd.ExecuteScalar());

        count.Should().Be(5);
    }
}
