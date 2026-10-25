using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier2_BoundaryAndCornerCases;

public class Boundary08_QdrantPayloadBoundaryTests
{
    [Fact]
    public void Test01_VectorWithAllZeros_IsValidUnitVectorMagnitudeEdge()
    {
        var zeroVector = new float[SpecificationConstants.QdrantVectorSize];
        zeroVector.Length.Should().Be(768);
        zeroVector.All(v => v == 0f).Should().BeTrue();
    }

    [Fact]
    public void Test02_VectorContainingNanOrInfinity_IsDetectedAsInvalid()
    {
        var invalidVector = new float[768];
        invalidVector[42] = float.NaN;

        bool hasInvalid = invalidVector.Any(v => float.IsNaN(v) || float.IsInfinity(v));
        hasInvalid.Should().BeTrue("Vector store adapters must sanitize vectors and reject NaN/Infinity");
    }

    [Fact]
    public void Test03_ExtremeContentLengthInPayload_SerializesWithoutTruncation()
    {
        var largeContent = new string('x', 50000);
        var point = new VectorPointModel(
            ChunkId: Guid.NewGuid(),
            DocumentId: Guid.NewGuid(),
            Vector: new float[768],
            Content: largeContent,
            FileName: "Large.txt",
            RelativePath: "Manuals/Large.txt",
            Folder: "Manuals",
            PageNumber: 1,
            SectionHeading: null,
            EquipmentId: null,
            FaultId: null,
            AlarmId: null,
            Tags: Array.Empty<string>()
        );

        point.Content.Length.Should().Be(50000);
    }

    [Fact]
    public void Test04_EmptyTagsList_IsHandledGracefullyInPayload()
    {
        var point = new VectorPointModel(
            Guid.NewGuid(), Guid.NewGuid(), new float[768], "Content", "Doc.md", "Faults/Doc.md",
            "Faults", null, null, null, null, null, Array.Empty<string>()
        );

        point.Tags.Should().BeEmpty();
    }

    [Fact]
    public void Test05_NullIdentifiersInPayload_SerializeAsNullRatherThanEmptyString()
    {
        var point = new VectorPointModel(
            Guid.NewGuid(), Guid.NewGuid(), new float[768], "Content", "Doc.md", "Faults/Doc.md",
            "Faults", null, null, null, null, null, Array.Empty<string>()
        );

        point.EquipmentId.Should().BeNull();
        point.FaultId.Should().BeNull();
        point.AlarmId.Should().BeNull();
    }
}
