namespace CellAssistant.UnitTests.Domain;

using CellAssistant.Domain.Enums;
using CellAssistant.Domain.Services;
using CellAssistant.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

public class ConfidenceCalculatorTests
{
    private readonly ConfidenceCalculator _calculator = new();

    private static RetrievedChunk CreateChunk(double score, bool exactMatch = false)
    {
        return new RetrievedChunk(
            Content: "Diagnostic content",
            SimilarityScore: score,
            ExactIdentifierMatch: exactMatch,
            Source: new SourceReference(Guid.NewGuid(), "File.md", "Faults/File.md", "Faults", null, "Heading")
        );
    }

    [Fact]
    public void Calculate_EmptyOrNullChunks_ReturnsLowConfidenceWithZeroScore()
    {
        var (score1, level1) = _calculator.Calculate(null, hasExactIdentifierMatch: false);
        score1.Should().Be(0.0);
        level1.Should().Be(ConfidenceLevel.Low);

        var (score2, level2) = _calculator.Calculate(Array.Empty<RetrievedChunk>(), hasExactIdentifierMatch: false);
        score2.Should().Be(0.0);
        level2.Should().Be(ConfidenceLevel.Low);
    }

    [Fact]
    public void Calculate_ScoreAbove85_ReturnsHighConfidenceWithoutExactMatch()
    {
        var chunks = new[] { CreateChunk(0.88, false) };
        var (score, level) = _calculator.Calculate(chunks, hasExactIdentifierMatch: false);

        score.Should().Be(0.88);
        level.Should().Be(ConfidenceLevel.High);
    }

    [Fact]
    public void Calculate_ScoreAt85Boundary_ReturnsHighConfidence()
    {
        var chunks = new[] { CreateChunk(0.85, false) };
        var (score, level) = _calculator.Calculate(chunks, hasExactIdentifierMatch: false);

        score.Should().Be(0.85);
        level.Should().Be(ConfidenceLevel.High);
    }

    [Fact]
    public void Calculate_ScoreBelow85WithoutExactMatch_ReturnsMediumConfidence()
    {
        var chunks = new[] { CreateChunk(0.8499, false) };
        var (score, level) = _calculator.Calculate(chunks, hasExactIdentifierMatch: false);

        score.Should().Be(0.8499);
        level.Should().Be(ConfidenceLevel.Medium);
    }

    [Fact]
    public void Calculate_ScoreAbove75WithExactMatch_ReturnsHighConfidence()
    {
        var chunks = new[] { CreateChunk(0.78, false) };
        var (score, level) = _calculator.Calculate(chunks, hasExactIdentifierMatch: true);

        score.Should().Be(0.78);
        level.Should().Be(ConfidenceLevel.High);
    }

    [Fact]
    public void Calculate_ScoreAt75BoundaryWithExactMatch_ReturnsHighConfidence()
    {
        var chunks = new[] { CreateChunk(0.75, false) };
        var (score, level) = _calculator.Calculate(chunks, hasExactIdentifierMatch: true);

        score.Should().Be(0.75);
        level.Should().Be(ConfidenceLevel.High);
    }

    [Fact]
    public void Calculate_ScoreBelow75WithExactMatch_ReturnsMediumConfidence()
    {
        var chunks = new[] { CreateChunk(0.7499, false) };
        var (score, level) = _calculator.Calculate(chunks, hasExactIdentifierMatch: true);

        score.Should().Be(0.7499);
        level.Should().Be(ConfidenceLevel.Medium);
    }

    [Fact]
    public void Calculate_ScoreAt70Boundary_ReturnsMediumConfidence()
    {
        var chunks = new[] { CreateChunk(0.70, false) };
        var (score, level) = _calculator.Calculate(chunks, hasExactIdentifierMatch: false);

        score.Should().Be(0.70);
        level.Should().Be(ConfidenceLevel.Medium);
    }

    [Fact]
    public void Calculate_ScoreBelow70_ReturnsLowConfidence()
    {
        var chunks = new[] { CreateChunk(0.6999, false) };
        var (score, level) = _calculator.Calculate(chunks, hasExactIdentifierMatch: false);

        score.Should().Be(0.6999);
        level.Should().Be(ConfidenceLevel.Low);
    }

    [Fact]
    public void Calculate_ChunkWithExactMatchFlag_ElevatesConfidence()
    {
        var chunks = new[] { CreateChunk(0.78, exactMatch: true) };
        var (score, level) = _calculator.Calculate(chunks, hasExactIdentifierMatch: false);

        score.Should().Be(0.78);
        level.Should().Be(ConfidenceLevel.High);
    }

    [Fact]
    public void Calculate_MultipleChunks_SelectsMaxScore()
    {
        var chunks = new[]
        {
            CreateChunk(0.60, false),
            CreateChunk(0.82, false),
            CreateChunk(0.71, false)
        };

        var (score, level) = _calculator.Calculate(chunks, hasExactIdentifierMatch: false);
        score.Should().Be(0.82);
        level.Should().Be(ConfidenceLevel.Medium);
    }

    [Fact]
    public void CalculateLevel_ConvenienceMethod_ReturnsSameLevel()
    {
        var chunks = new[] { CreateChunk(0.91, false) };
        _calculator.CalculateLevel(chunks).Should().Be(ConfidenceLevel.High);
    }
}
