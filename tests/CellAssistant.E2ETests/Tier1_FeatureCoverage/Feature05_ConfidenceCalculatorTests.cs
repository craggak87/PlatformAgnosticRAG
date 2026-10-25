using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier1_FeatureCoverage;

public class Feature05_ConfidenceCalculatorTests
{
    private static RetrievedChunkModel CreateChunk(double score, bool exactMatch = false)
    {
        return new RetrievedChunkModel(
            Content: "Diagnostic content",
            SimilarityScore: score,
            ExactIdentifierMatch: exactMatch,
            Source: new SourceReferenceDto(Guid.NewGuid(), "File.md", "Faults/File.md", null, "Heading")
        );
    }

    [Fact]
    public void Test01_HighScore_AboveThreshold_ReturnsHighConfidence()
    {
        var chunks = new[] { CreateChunk(0.88, false) };
        var (score, level) = E2ETestHarness.CalculateConfidence(chunks, hasExactIdentifierMatch: false);

        score.Should().Be(0.88);
        level.Should().Be(ConfidenceLevel.High);
    }

    [Fact]
    public void Test02_ExactIdentifierMatch_BoostsConfidenceThresholdTo075()
    {
        var chunks = new[] { CreateChunk(0.78, exactMatch: true) };
        var (score, level) = E2ETestHarness.CalculateConfidence(chunks, hasExactIdentifierMatch: true);

        score.Should().Be(0.78);
        level.Should().Be(ConfidenceLevel.High);
    }

    [Fact]
    public void Test03_ModerateScore_WithoutExactMatch_ReturnsMediumConfidence()
    {
        var chunks = new[] { CreateChunk(0.78, exactMatch: false) };
        var (score, level) = E2ETestHarness.CalculateConfidence(chunks, hasExactIdentifierMatch: false);

        score.Should().Be(0.78);
        level.Should().Be(ConfidenceLevel.Medium);
    }

    [Fact]
    public void Test04_LowScore_Below070_ReturnsLowConfidence()
    {
        var chunks = new[] { CreateChunk(0.65, exactMatch: false) };
        var (score, level) = E2ETestHarness.CalculateConfidence(chunks, hasExactIdentifierMatch: false);

        score.Should().Be(0.65);
        level.Should().Be(ConfidenceLevel.Low);
    }

    [Fact]
    public void Test05_EmptyRetrievedChunks_ReturnsZeroScoreAndLowConfidence()
    {
        var chunks = Array.Empty<RetrievedChunkModel>();
        var (score, level) = E2ETestHarness.CalculateConfidence(chunks, hasExactIdentifierMatch: false);

        score.Should().Be(0.0);
        level.Should().Be(ConfidenceLevel.Low);
    }
}
