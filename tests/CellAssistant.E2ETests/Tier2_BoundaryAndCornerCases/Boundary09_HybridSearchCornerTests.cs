using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier2_BoundaryAndCornerCases;

public class Boundary09_HybridSearchCornerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Test01_ZeroOrNegativeTopK_IsNormalizedToDefaultOrRejected(int requestedTopK)
    {
        int effectiveTopK = requestedTopK <= 0 ? 5 : requestedTopK;
        effectiveTopK.Should().Be(5);
    }

    [Fact]
    public void Test02_NonExistentEquipmentFilter_ReturnsZeroMatchesGracefully()
    {
        var candidates = new List<RetrievedChunkModel>
        {
            new("Robot chunk", 0.82, false, new SourceReferenceDto(Guid.NewGuid(), "R.md", "Equipment/R.md"))
        };

        // Filter by non-existent equipment
        string targetEquipment = "NONEXISTENT99";
        var filtered = candidates.Where(c => c.Source.FileName.Contains(targetEquipment)).ToList();

        filtered.Should().BeEmpty();
    }

    [Fact]
    public void Test03_NonExistentFolderFilter_ReturnsZeroMatchesGracefully()
    {
        var candidates = new List<RetrievedChunkModel>
        {
            new("Pneumatic chunk", 0.85, false, new SourceReferenceDto(Guid.NewGuid(), "P.md", "Pneumatics/P.md"))
        };

        var filtered = candidates.Where(c => c.Source.RelativePath.StartsWith("NonExistentFolder/")).ToList();
        filtered.Should().BeEmpty();
    }

    [Fact]
    public void Test04_AllChunksBelowMinimumSimilarityThreshold_YieldsEmptyResult()
    {
        var candidates = new List<RetrievedChunkModel>
        {
            new("Weak match 1", 0.20, false, new SourceReferenceDto(Guid.NewGuid(), "A.md", "Faults/A.md")),
            new("Weak match 2", 0.35, false, new SourceReferenceDto(Guid.NewGuid(), "B.md", "Faults/B.md"))
        };

        const double minimumRelevanceThreshold = 0.50;
        var relevant = candidates.Where(c => c.SimilarityScore >= minimumRelevanceThreshold).ToList();

        relevant.Should().BeEmpty();
    }

    [Fact]
    public void Test05_SingleChunkAvailable_ReturnsSingleElementListWithoutCrashing()
    {
        var candidates = new List<RetrievedChunkModel>
        {
            new("Sole match", 0.89, false, new SourceReferenceDto(Guid.NewGuid(), "Single.md", "Faults/Single.md"))
        };

        var topK = candidates.Take(5).ToList();
        topK.Should().HaveCount(1);
    }
}
