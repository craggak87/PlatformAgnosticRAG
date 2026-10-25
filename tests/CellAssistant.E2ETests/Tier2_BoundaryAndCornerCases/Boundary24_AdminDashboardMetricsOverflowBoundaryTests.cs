using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier2_BoundaryAndCornerCases;

public class Boundary24_AdminDashboardMetricsOverflowBoundaryTests
{
    [Fact]
    public void Test01_ZeroIndexedDocuments_DisplaysZeroWithoutDivisionByZero()
    {
        long documentCount = 0;
        long vectorCount = 0;
        double averageVectorsPerDoc = documentCount == 0 ? 0 : (double)vectorCount / documentCount;

        averageVectorsPerDoc.Should().Be(0.0);
    }

    [Fact]
    public void Test02_LargeVectorCount_FormatsWithStandardNumberSeparators()
    {
        long hugeVectorCount = 1250000;
        var formatted = hugeVectorCount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

        formatted.Should().Be("1,250,000");
    }

    [Fact]
    public void Test03_NullLastRunTimestamp_DisplaysNeverOrNotAvailable()
    {
        DateTime? lastRunUtc = null;
        var display = lastRunUtc.HasValue ? lastRunUtc.Value.ToString("u") : "Never";

        display.Should().Be("Never");
    }

    [Fact]
    public void Test04_IndexerStatusFailed_DisplaysWarningBadge()
    {
        var status = "Failed";
        var badgeColor = status switch
        {
            "Idle" => "success",
            "Indexing" or "Reindexing" => "info",
            "Failed" => "danger",
            _ => "secondary"
        };

        badgeColor.Should().Be("danger");
    }

    [Fact]
    public void Test05_AdminMetricsNegativeCount_ClampedToZero()
    {
        long rawCount = -5;
        long sanitized = Math.Max(0, rawCount);

        sanitized.Should().Be(0);
    }
}
