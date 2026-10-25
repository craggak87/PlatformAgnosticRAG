using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier2_BoundaryAndCornerCases;

public class Boundary05_ConfidenceThresholdsBoundaryTests
{
    private static RetrievedChunkModel CreateChunk(double score, bool exactMatch = false)
    {
        return new RetrievedChunkModel("Text", score, exactMatch, new SourceReferenceDto(Guid.NewGuid(), "F", "P"));
    }

    [Theory]
    [InlineData(0.8500, ConfidenceLevel.High)]
    [InlineData(0.8501, ConfidenceLevel.High)]
    [InlineData(0.8499, ConfidenceLevel.Medium)]
    public void Test01_NormalConfidenceBoundary_At085_YieldsExactLevels(double score, ConfidenceLevel expected)
    {
        var chunks = new[] { CreateChunk(score, exactMatch: false) };
        var (_, level) = E2ETestHarness.CalculateConfidence(chunks, hasExactIdentifierMatch: false);

        level.Should().Be(expected);
    }

    [Theory]
    [InlineData(0.7500, ConfidenceLevel.High)]
    [InlineData(0.7501, ConfidenceLevel.High)]
    [InlineData(0.7499, ConfidenceLevel.Medium)]
    public void Test02_ExactMatchConfidenceBoundary_At075_YieldsExactLevels(double score, ConfidenceLevel expected)
    {
        var chunks = new[] { CreateChunk(score, exactMatch: true) };
        var (_, level) = E2ETestHarness.CalculateConfidence(chunks, hasExactIdentifierMatch: true);

        level.Should().Be(expected);
    }

    [Theory]
    [InlineData(0.7000, ConfidenceLevel.Medium)]
    [InlineData(0.7001, ConfidenceLevel.Medium)]
    [InlineData(0.6999, ConfidenceLevel.Low)]
    public void Test03_MediumConfidenceBoundary_At070_YieldsExactLevels(double score, ConfidenceLevel expected)
    {
        var chunks = new[] { CreateChunk(score, exactMatch: false) };
        var (_, level) = E2ETestHarness.CalculateConfidence(chunks, hasExactIdentifierMatch: false);

        level.Should().Be(expected);
    }

    [Fact]
    public void Test04_ZeroSimilarityScore_AlwaysReturnsLowConfidence()
    {
        var chunks = new[] { CreateChunk(0.0, exactMatch: false) };
        var (_, level) = E2ETestHarness.CalculateConfidence(chunks, hasExactIdentifierMatch: false);

        level.Should().Be(ConfidenceLevel.Low);
    }

    [Fact]
    public void Test05_SingleChunkWithExactMatchOver075_ElevatesEntireQueryConfidence()
    {
        var chunks = new[]
        {
            CreateChunk(0.50, exactMatch: false),
            CreateChunk(0.76, exactMatch: true),
            CreateChunk(0.60, exactMatch: false)
        };

        var (score, level) = E2ETestHarness.CalculateConfidence(chunks, hasExactIdentifierMatch: true);
        score.Should().Be(0.76);
        level.Should().Be(ConfidenceLevel.High);
    }
}
