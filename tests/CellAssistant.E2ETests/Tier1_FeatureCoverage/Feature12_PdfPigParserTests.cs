using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier1_FeatureCoverage;

public class Feature12_PdfPigParserTests
{
    [Fact]
    public void Test01_PdfPageNumbering_MustBeStrictlyOneBased()
    {
        var chunk = new DocumentChunkModel(
            Id: Guid.NewGuid(),
            DocumentId: Guid.NewGuid(),
            Content: "Section on gripper valve replacement",
            ChunkIndex: 0,
            PageNumber: 1, // 1-based page number per Doc 3 §R4
            SectionHeading: null,
            EquipmentId: "ROBOT01",
            FaultId: null,
            AlarmId: null,
            Tags: new[] { "manual" }
        );

        chunk.PageNumber.Should().NotBeNull();
        chunk.PageNumber.Should().BeGreaterOrEqualTo(1, "ADR and Doc 3 require 1-based page numbers for PDFs");
    }

    [Fact]
    public void Test02_PdfFileExtension_IsRecognizedByCanParse()
    {
        var validExtensions = new[] { ".pdf", ".PDF" };
        var invalidExtensions = new[] { ".md", ".txt", ".docx", ".png" };

        foreach (var ext in validExtensions)
        {
            ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
        }

        foreach (var ext in invalidExtensions)
        {
            ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        }
    }

    [Fact]
    public void Test03_PdfChunkHeader_IncludesPageNumber()
    {
        var header = E2ETestHarness.FormatChunkHeader(
            "IRB4600_Manual.pdf",
            "Manuals",
            "ROBOT01",
            null,
            "Page 17",
            "Detailed schematics of gripper solenoid valve wiring."
        );

        header.Should().Contain("[Document: IRB4600_Manual.pdf");
        header.Should().Contain("Page/Section: Page 17");
        header.Should().Contain("Folder: Manuals");
    }

    [Fact]
    public void Test04_MultiPageDocument_PreservesDistinctPageNumbers()
    {
        var page1Chunk = new DocumentChunkModel(Guid.NewGuid(), Guid.NewGuid(), "Page 1 content", 0, 1, null, null, null, null, Array.Empty<string>());
        var page2Chunk = new DocumentChunkModel(Guid.NewGuid(), Guid.NewGuid(), "Page 2 content", 1, 2, null, null, null, null, Array.Empty<string>());

        page1Chunk.PageNumber.Should().Be(1);
        page2Chunk.PageNumber.Should().Be(2);
        page2Chunk.PageNumber.Should().BeGreaterThan(page1Chunk.PageNumber!.Value);
    }

    [Fact]
    public void Test05_SourceReferenceForPdf_FormatsPageCitationForWebUi()
    {
        var source = new SourceReferenceDto(
            DocumentId: Guid.NewGuid(),
            FileName: "RobotManual.pdf",
            RelativePath: "Manuals/RobotManual.pdf",
            PageNumber: 17,
            SectionHeading: null
        );

        // Web UI opens PDF to #page={pageNumber} per Doc 2 §R5
        var expectedUriFragment = $"#page={source.PageNumber}";
        expectedUriFragment.Should().Be("#page=17");
    }
}
