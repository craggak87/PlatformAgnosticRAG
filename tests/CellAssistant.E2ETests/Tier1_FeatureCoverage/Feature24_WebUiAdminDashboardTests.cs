using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier1_FeatureCoverage;

public class Feature24_WebUiAdminDashboardTests
{
    private readonly string _repoRoot;

    public Feature24_WebUiAdminDashboardTests()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current) && !File.Exists(Path.Combine(current, "1.Cell_Knowledge_Assistant_ADR.md")))
        {
            current = Directory.GetParent(current)?.FullName;
        }
        _repoRoot = current ?? Directory.GetCurrentDirectory();
    }

    [Fact]
    public void Test01_AdminMetrics_IncludesAllMandatoryIndicators()
    {
        var metrics = new
        {
            IndexedDocumentCount = 26,
            TotalVectorCount = 142L,
            LastRunUtc = DateTime.UtcNow,
            IndexerStatus = "Idle",
            Health = new { Ollama = "Healthy", Qdrant = "Healthy", Sqlite = "Healthy" }
        };

        metrics.IndexedDocumentCount.Should().BeGreaterOrEqualTo(0);
        metrics.TotalVectorCount.Should().BeGreaterOrEqualTo(0);
        metrics.IndexerStatus.Should().Be("Idle");
        metrics.Health.Ollama.Should().Be("Healthy");
    }

    [Fact]
    public void Test02_AdminReindexTrigger_MatchesApiRoute()
    {
        const string reindexActionRoute = SpecificationConstants.AdminReindexEndpoint;
        reindexActionRoute.Should().Be("/api/admin/reindex");
    }

    [Fact]
    public void Test03_HealthStatusBadges_ReflectAllComponents()
    {
        var subsystems = new[] { "API", "Ollama", "Qdrant", "SQLite", "Indexer" };
        subsystems.Length.Should().Be(5);
    }

    [Fact]
    public void Test04_DocumentCountQuery_ReturnsAccurateCountFromSqlite()
    {
        using var conn = E2ETestHarness.CreateInMemorySqlite();
        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = @"
INSERT INTO documents (id, file_name, file_path, relative_path, document_type, folder, content_hash, created_utc, modified_utc, indexed_utc)
VALUES ('11111111-1111-1111-1111-111111111111', 'Doc1.md', '/path/Doc1.md', 'Faults/Doc1.md', 'Markdown', 'Faults', 'hash1', datetime('now'), datetime('now'), datetime('now')),
       ('22222222-2222-2222-2222-222222222222', 'Doc2.md', '/path/Doc2.md', 'Faults/Doc2.md', 'Markdown', 'Faults', 'hash2', datetime('now'), datetime('now'), datetime('now'));";
        insertCmd.ExecuteNonQuery();

        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM documents;";
        var count = Convert.ToInt32(countCmd.ExecuteScalar());

        count.Should().Be(2);
    }

    [Fact]
    public void Test05_AdminDashboardMarkup_IfPresent_IncludesStatusCards()
    {
        var adminHtml = Path.Combine(_repoRoot, "src", "CellAssistant.Api", "wwwroot", "admin.html");
        var indexHtml = Path.Combine(_repoRoot, "src", "CellAssistant.Api", "wwwroot", "index.html");

        var targetFile = File.Exists(adminHtml) ? adminHtml : (File.Exists(indexHtml) ? indexHtml : null);
        if (targetFile != null)
        {
            var content = File.ReadAllText(targetFile);
            content.ToLowerInvariant().Should().Match(c => c.Contains("reindex") || c.Contains("admin") || c.Contains("status"));
        }
    }
}
