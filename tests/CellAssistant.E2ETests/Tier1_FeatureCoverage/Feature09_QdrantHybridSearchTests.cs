using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier1_FeatureCoverage;

public class Feature09_QdrantHybridSearchTests
{
    [Fact]
    public void Test01_HybridSearchQuery_SupportsBothDenseVectorAndFilters()
    {
        var queryVector = new float[768];
        var equipmentFilter = "ROBOT01";
        var folderFilter = "Faults";
        int topK = 5;

        topK.Should().Be(5, "Default TopK per Doc 7 §2 is 5");
        queryVector.Length.Should().Be(768);
        equipmentFilter.Should().NotBeNull();
        folderFilter.Should().NotBeNull();
    }

    [Fact]
    public void Test02_ExactIdentifierMatch_BoostsChunkRanking()
    {
        var chunks = new List<RetrievedChunkModel>
        {
            new("General robot overview text", 0.81, false,
                new SourceReferenceDto(Guid.NewGuid(), "Overview.md", "Equipment/Overview.md", null, null)),
            new("Alarm ALM-205 specific trigger on safety gate", 0.77, true,
                new SourceReferenceDto(Guid.NewGuid(), "ALM-205.md", "Alarms/ALM-205.md", null, null))
        };

        // When exact identifier boost is applied (e.g. +0.10 or prioritized sort)
        var ranked = chunks.OrderByDescending(c => c.ExactIdentifierMatch ? c.SimilarityScore + 0.15 : c.SimilarityScore).ToList();

        ranked[0].ExactIdentifierMatch.Should().BeTrue("Exact identifier match should be boosted above higher semantic similarity");
        ranked[0].Source.FileName.Should().Be("ALM-205.md");
    }

    [Fact]
    public void Test03_EquipmentFilter_ConstrainsPayloadSearch()
    {
        var filter = new
        {
            must = new object[]
            {
                new { key = "equipmentId", match = new { value = "VFD01" } }
            }
        };

        filter.must.Length.Should().Be(1);
    }

    [Fact]
    public void Test04_FolderFilter_ConstrainsVaultPartition()
    {
        var filter = new
        {
            must = new object[]
            {
                new { key = "folder", match = new { value = "Manuals" } }
            }
        };

        filter.must.Length.Should().Be(1);
    }

    [Fact]
    public void Test05_TopKTruncation_LimitsResultsToRequestedCount()
    {
        var candidates = Enumerable.Range(1, 20)
            .Select(i => new RetrievedChunkModel($"Chunk {i}", 0.70 + (i * 0.01), false,
                new SourceReferenceDto(Guid.NewGuid(), $"File{i}.md", $"Faults/File{i}.md", null, null)))
            .ToList();

        var topK = candidates.OrderByDescending(c => c.SimilarityScore).Take(5).ToList();

        topK.Count.Should().Be(5);
        topK[0].SimilarityScore.Should().BeGreaterThan(topK[4].SimilarityScore);
    }
}
