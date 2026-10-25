using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier2_BoundaryAndCornerCases;

public class Boundary01_DomainEntitiesBoundaryTests
{
    [Fact]
    public void Test01_Document_WithNullOptionalFields_InstantiatesSafely()
    {
        var doc = new DocumentModel(
            Id: Guid.NewGuid(),
            FileName: "Note.md",
            FilePath: "/data/KnowledgeBase/Note.md",
            RelativePath: "Note.md",
            DocumentType: DocumentType.Markdown,
            Folder: "Procedures",
            ContentHash: "hash123",
            EquipmentId: null,
            Manufacturer: null,
            Criticality: null,
            Tags: Array.Empty<string>(),
            ChunkCount: 0,
            CreatedUtc: DateTime.MinValue,
            ModifiedUtc: DateTime.MaxValue,
            IndexedUtc: DateTime.UtcNow
        );

        doc.EquipmentId.Should().BeNull();
        doc.Manufacturer.Should().BeNull();
        doc.Tags.Should().BeEmpty();
        doc.ChunkCount.Should().Be(0);
    }

    [Fact]
    public void Test02_DocumentChunk_WithUnicodeCharactersAndLongContent_PreservesData()
    {
        var unicodeContent = "Testing German äöü, French éèê, Japanese コンベア, and emoji ⚙️ \u26A0\uFE0F";
        var chunk = new DocumentChunkModel(
            Id: Guid.NewGuid(),
            DocumentId: Guid.NewGuid(),
            Content: unicodeContent,
            ChunkIndex: 9999,
            PageNumber: null,
            SectionHeading: "Unicode & Symbols",
            EquipmentId: null,
            FaultId: null,
            AlarmId: null,
            Tags: new[] { "unicode", "test" }
        );

        chunk.Content.Should().Be(unicodeContent);
        chunk.ChunkIndex.Should().Be(9999);
    }

    [Fact]
    public void Test03_SourceReference_WithPageNumberAtIntMax_HandledSafely()
    {
        var source = new SourceReferenceDto(
            DocumentId: Guid.NewGuid(),
            FileName: "BigManual.pdf",
            RelativePath: "Manuals/BigManual.pdf",
            PageNumber: int.MaxValue,
            SectionHeading: null
        );

        source.PageNumber.Should().Be(int.MaxValue);
    }

    [Fact]
    public void Test04_RetrievedChunk_WithBoundaryZeroAndOneSimilarityScores()
    {
        var chunkZero = new RetrievedChunkModel("Text", 0.0, false, new SourceReferenceDto(Guid.NewGuid(), "F", "P"));
        var chunkOne = new RetrievedChunkModel("Text", 1.0, true, new SourceReferenceDto(Guid.NewGuid(), "F", "P"));

        chunkZero.SimilarityScore.Should().Be(0.0);
        chunkOne.SimilarityScore.Should().Be(1.0);
    }

    [Fact]
    public void Test05_Tags_WithSpecialCharactersAndHyphens_AreRetained()
    {
        var tags = new[] { "safety-gate-2", "vfd_01", "380v_ac", "i/o-mapping" };
        var doc = new DocumentModel(
            Guid.NewGuid(), "Doc.md", "/p", "Doc.md", DocumentType.Markdown, "Safety", "hash",
            null, null, null, tags, 1, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow
        );

        doc.Tags.Should().BeEquivalentTo(tags);
    }
}
