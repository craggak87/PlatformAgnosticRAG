namespace CellAssistant.UnitTests.Domain;

using CellAssistant.Domain.Entities;
using CellAssistant.Domain.Enums;
using CellAssistant.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

public class EntitiesAndValueObjectsTests
{
    [Fact]
    public void Document_ShouldInitializeWithDefaultValues()
    {
        var doc = new Document();

        doc.Id.Should().NotBeEmpty();
        doc.FileName.Should().BeEmpty();
        doc.FilePath.Should().BeEmpty();
        doc.RelativePath.Should().BeEmpty();
        doc.Folder.Should().BeEmpty();
        doc.ContentHash.Should().BeEmpty();
        doc.EquipmentId.Should().BeNull();
        doc.Manufacturer.Should().BeNull();
        doc.Criticality.Should().BeNull();
        doc.Tags.Should().NotBeNull().And.BeEmpty();
        doc.ChunkCount.Should().Be(0);
        doc.CreatedUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
        doc.ModifiedUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
        doc.IndexedUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Document_PropertyAssignment_ShouldPersistValues()
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var tags = new List<string> { "robot", "gripper", "flt-001" };

        var doc = new Document
        {
            Id = id,
            FileName = "Robot Gripper.md",
            FilePath = "/data/KnowledgeBase/Faults/Robot Gripper.md",
            RelativePath = "Faults/Robot Gripper.md",
            DocumentType = DocumentType.Markdown,
            Folder = "Faults",
            ContentHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            EquipmentId = "ROBOT01",
            Manufacturer = "ABB",
            Criticality = "High",
            Tags = tags,
            ChunkCount = 4,
            CreatedUtc = now,
            ModifiedUtc = now,
            IndexedUtc = now
        };

        doc.Id.Should().Be(id);
        doc.FileName.Should().Be("Robot Gripper.md");
        doc.FilePath.Should().Be("/data/KnowledgeBase/Faults/Robot Gripper.md");
        doc.RelativePath.Should().Be("Faults/Robot Gripper.md");
        doc.DocumentType.Should().Be(DocumentType.Markdown);
        doc.Folder.Should().Be("Faults");
        doc.ContentHash.Should().Be("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
        doc.EquipmentId.Should().Be("ROBOT01");
        doc.Manufacturer.Should().Be("ABB");
        doc.Criticality.Should().Be("High");
        doc.Tags.Should().BeEquivalentTo(tags);
        doc.ChunkCount.Should().Be(4);
        doc.CreatedUtc.Should().Be(now);
        doc.ModifiedUtc.Should().Be(now);
        doc.IndexedUtc.Should().Be(now);
    }

    [Fact]
    public void DocumentChunk_ShouldHoldCorrectProperties()
    {
        var chunkId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        var tags = new List<string> { "gripper", "sensor" };

        var chunk = new DocumentChunk
        {
            Id = chunkId,
            DocumentId = docId,
            Content = "[Document: Robot Gripper.md | Folder: Faults]\nSensor pinched.",
            ChunkIndex = 2,
            PageNumber = 17,
            SectionHeading = "Fault Recovery",
            EquipmentId = "ROBOT01",
            FaultId = "FLT-001",
            AlarmId = "ALM-205",
            Tags = tags
        };

        chunk.Id.Should().Be(chunkId);
        chunk.DocumentId.Should().Be(docId);
        chunk.Content.Should().Contain("Sensor pinched.");
        chunk.ChunkIndex.Should().Be(2);
        chunk.PageNumber.Should().Be(17);
        chunk.SectionHeading.Should().Be("Fault Recovery");
        chunk.EquipmentId.Should().Be("ROBOT01");
        chunk.FaultId.Should().Be("FLT-001");
        chunk.AlarmId.Should().Be("ALM-205");
        chunk.Tags.Should().BeEquivalentTo(tags);
    }

    [Fact]
    public void DocumentChunk_NullableProperties_ShouldAcceptNull()
    {
        var chunk = new DocumentChunk
        {
            PageNumber = null,
            SectionHeading = null,
            EquipmentId = null,
            FaultId = null,
            AlarmId = null
        };

        chunk.PageNumber.Should().BeNull();
        chunk.SectionHeading.Should().BeNull();
        chunk.EquipmentId.Should().BeNull();
        chunk.FaultId.Should().BeNull();
        chunk.AlarmId.Should().BeNull();
    }

    [Fact]
    public void VectorPoint_ShouldSupport768DimensionVectorAndPayload()
    {
        var chunkId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        var vector = new float[768];
        Array.Fill(vector, 0.042f);

        var point = new VectorPoint
        {
            ChunkId = chunkId,
            DocumentId = docId,
            Vector = vector,
            Content = "Vectorized chunk content",
            FileName = "Manual.pdf",
            RelativePath = "Manuals/Manual.pdf",
            Folder = "Manuals",
            PageNumber = 14,
            SectionHeading = "Wiring Diagrams",
            EquipmentId = "PLC01",
            FaultId = null,
            AlarmId = null,
            Tags = new List<string> { "plc", "wiring" }
        };

        point.ChunkId.Should().Be(chunkId);
        point.DocumentId.Should().Be(docId);
        point.Vector.Length.Should().Be(768);
        point.Vector[0].Should().Be(0.042f);
        point.Content.Should().Be("Vectorized chunk content");
        point.FileName.Should().Be("Manual.pdf");
        point.RelativePath.Should().Be("Manuals/Manual.pdf");
        point.Folder.Should().Be("Manuals");
        point.PageNumber.Should().Be(14);
        point.SectionHeading.Should().Be("Wiring Diagrams");
        point.EquipmentId.Should().Be("PLC01");
    }

    [Fact]
    public void SourceReference_ValueEquality_ShouldFollowRecordSemantics()
    {
        var docId = Guid.NewGuid();
        var ref1 = new SourceReference(docId, "Manual.pdf", "Manuals/Manual.pdf", "Manuals", 14, "Section 1");
        var ref2 = new SourceReference(docId, "Manual.pdf", "Manuals/Manual.pdf", "Manuals", 14, "Section 1");
        var ref3 = new SourceReference(docId, "Manual.pdf", "Manuals/Manual.pdf", "Manuals", 15, "Section 1");

        ref1.Should().Be(ref2);
        (ref1 == ref2).Should().BeTrue();
        (ref1 != ref3).Should().BeTrue();
        ref1.GetHashCode().Should().Be(ref2.GetHashCode());
    }

    [Fact]
    public void RetrievedChunk_RecordSemantics_ShouldHoldCorrectValues()
    {
        var source = new SourceReference(Guid.NewGuid(), "Gripper.md", "Faults/Gripper.md", "Faults");
        var chunk = new RetrievedChunk("Chunk text", 0.89, true, source);

        chunk.Content.Should().Be("Chunk text");
        chunk.SimilarityScore.Should().Be(0.89);
        chunk.ExactIdentifierMatch.Should().BeTrue();
        chunk.Source.Should().Be(source);
    }

    [Fact]
    public void SearchHistoryEntry_ShouldInitializeProperly()
    {
        var id = Guid.NewGuid();
        var source = new SourceReference(Guid.NewGuid(), "File.md", "Faults/File.md", "Faults");
        var entry = new SearchHistoryEntry
        {
            Id = id,
            Question = "What causes ALM-205?",
            Answer = "Sensor harness pinched.",
            Confidence = ConfidenceLevel.High,
            Sources = new List<SourceReference> { source }
        };

        entry.Id.Should().Be(id);
        entry.Question.Should().Be("What causes ALM-205?");
        entry.Answer.Should().Be("Sensor harness pinched.");
        entry.Confidence.Should().Be(ConfidenceLevel.High);
        entry.Sources.Should().ContainSingle().Which.Should().Be(source);
        entry.AskedUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void IndexerState_ShouldEnforceSingleRowInvariant()
    {
        var state = new IndexerState();

        state.Id.Should().Be(1);
        state.Status.Should().Be(IndexerStatus.Idle);
        state.LastRunUtc.Should().BeNull();
        state.LastError.Should().BeNull();
        state.ReindexRequested.Should().BeFalse();
    }

    [Fact]
    public void Enums_ShouldContainExpectedValues()
    {
        Enum.GetNames<DocumentType>().Should().BeEquivalentTo("Markdown", "Pdf", "Text");
        Enum.GetNames<ConfidenceLevel>().Should().BeEquivalentTo("High", "Medium", "Low");
        Enum.GetNames<CriticalityLevel>().Should().BeEquivalentTo("High", "Medium", "Low");
        Enum.GetNames<IndexerStatus>().Should().Contain(new[] { "Idle", "Indexing", "Reindexing", "Failed", "Degraded" });
    }
}
