using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier1_FeatureCoverage;

public class Feature01_DomainEntitiesTests
{
    [Fact]
    public void Test01_DocumentEntity_CanInstantiateWithRequiredFields()
    {
        var id = Guid.NewGuid();
        var doc = new DocumentModel(
            Id: id,
            FileName: "Robot Gripper.md",
            FilePath: "/data/KnowledgeBase/Faults/Robot Gripper.md",
            RelativePath: "Faults/Robot Gripper.md",
            DocumentType: DocumentType.Markdown,
            Folder: "Faults",
            ContentHash: "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            EquipmentId: "ROBOT01",
            Manufacturer: "ABB",
            Criticality: "High",
            Tags: new[] { "robot", "gripper", "fault" },
            ChunkCount: 3,
            CreatedUtc: DateTime.UtcNow,
            ModifiedUtc: DateTime.UtcNow,
            IndexedUtc: DateTime.UtcNow
        );

        doc.Id.Should().Be(id);
        doc.DocumentType.Should().Be(DocumentType.Markdown);
        doc.Tags.Should().Contain("gripper");
        doc.ChunkCount.Should().Be(3);
    }

    [Fact]
    public void Test02_DocumentChunkEntity_MaintainsParentDocumentReferenceAndIndex()
    {
        var parentId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();

        var chunk = new DocumentChunkModel(
            Id: chunkId,
            DocumentId: parentId,
            Content: "Sample chunk content",
            ChunkIndex: 0,
            PageNumber: null,
            SectionHeading: "Fault Symptoms",
            EquipmentId: "ROBOT01",
            FaultId: "FLT-001",
            AlarmId: "ALM-205",
            Tags: new[] { "fault", "gripper" }
        );

        chunk.DocumentId.Should().Be(parentId);
        chunk.ChunkIndex.Should().Be(0);
        chunk.SectionHeading.Should().Be("Fault Symptoms");
        chunk.FaultId.Should().Be("FLT-001");
    }

    [Fact]
    public void Test03_VectorPointEntity_Matches768DimensionContract()
    {
        var chunkId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        var vector = new float[SpecificationConstants.QdrantVectorSize];
        Array.Fill(vector, 0.05f);

        var point = new VectorPointModel(
            ChunkId: chunkId,
            DocumentId: docId,
            Vector: vector,
            Content: "Context enriched chunk",
            FileName: "Manual.pdf",
            RelativePath: "Manuals/Manual.pdf",
            Folder: "Manuals",
            PageNumber: 12,
            SectionHeading: null,
            EquipmentId: "ROBOT01",
            FaultId: null,
            AlarmId: null,
            Tags: new[] { "manual" }
        );

        point.Vector.Length.Should().Be(768);
        point.PageNumber.Should().Be(12);
        point.Folder.Should().Be("Manuals");
    }

    [Fact]
    public void Test04_SourceReferenceValueObject_ContainsMandatoryTraceabilityFields()
    {
        var docId = Guid.NewGuid();
        var source = new SourceReferenceDto(
            DocumentId: docId,
            FileName: "ALM-205 Safety Gate Open.md",
            RelativePath: "Alarms/ALM-205 Safety Gate Open.md",
            PageNumber: null,
            SectionHeading: "Troubleshooting Steps"
        );

        source.DocumentId.Should().Be(docId);
        source.RelativePath.Should().Be("Alarms/ALM-205 Safety Gate Open.md");
        source.SectionHeading.Should().Be("Troubleshooting Steps");
    }

    [Fact]
    public void Test05_Enums_ContainExpectedSpecificationValues()
    {
        Enum.GetNames(typeof(ConfidenceLevel)).Should().BeEquivalentTo(new[] { "High", "Medium", "Low" });
        Enum.GetNames(typeof(DocumentType)).Should().BeEquivalentTo(new[] { "Markdown", "Pdf", "Text" });
        Enum.GetNames(typeof(CriticalityLevel)).Should().BeEquivalentTo(new[] { "High", "Medium", "Low" });
        Enum.GetNames(typeof(IndexerStatus)).Should().Contain(new[] { "Idle", "Indexing", "Reindexing", "Failed", "Degraded" });
    }
}
