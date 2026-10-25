namespace CellAssistant.UnitTests.Parsing;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CellAssistant.Domain.Enums;
using CellAssistant.Infrastructure.Parsing;
using FluentAssertions;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

public class PdfPigParserTests : IDisposable
{
    private readonly string _tempDir;
    private readonly PdfPigParser _parser;

    public PdfPigParserTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "pdfpig_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _parser = new PdfPigParser();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    private static byte[] CreateMinimalPdf(params string[] pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        foreach (var pageText in pages)
        {
            var page = builder.AddPage(PageSize.A4);
            page.AddText(pageText, 12, new PdfPoint(50, 750), font);
        }

        return builder.Build();
    }

    [Theory]
    [InlineData("manual.pdf", true)]
    [InlineData("MANUAL.PDF", true)]
    [InlineData("file.md", false)]
    [InlineData("file.txt", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void CanParse_ChecksExtensionCorrectly(string? path, bool expected)
    {
        _parser.CanParse(path!).Should().Be(expected);
    }

    [Fact]
    public async Task ParseAsync_OneBasedPageNumbering_StrictlyEnforced()
    {
        var pdfBytes = CreateMinimalPdf("First page text content", "Second page text content");
        var filePath = Path.Combine(_tempDir, "ROBOT01_Manual.pdf");
        await File.WriteAllBytesAsync(filePath, pdfBytes);

        var (document, chunks) = await _parser.ParseAsync(filePath, "Manuals/ROBOT01_Manual.pdf");

        document.DocumentType.Should().Be(DocumentType.Pdf);
        document.EquipmentId.Should().Be("ROBOT01");
        document.Folder.Should().Be("Manuals");

        chunks.Count.Should().Be(2);
        chunks[0].PageNumber.Should().Be(1);
        chunks[1].PageNumber.Should().Be(2);
        chunks[0].SectionHeading.Should().BeNull();
        chunks[0].Content.Should().Contain("Page/Section: Page 1");
        chunks[1].Content.Should().Contain("Page/Section: Page 2");
    }

    [Fact]
    public async Task ParseAsync_OversizedPage_SplitsWithSlidingWindow()
    {
        var words = string.Join(" ", Enumerable.Range(1, 800).Select(i => $"spec{i}"));
        var pdfBytes = CreateMinimalPdf(words);
        var filePath = Path.Combine(_tempDir, "VFD01_Specs.pdf");
        await File.WriteAllBytesAsync(filePath, pdfBytes);

        var (_, chunks) = await _parser.ParseAsync(filePath, "Manuals/VFD01_Specs.pdf");

        // 800 words on 1 page should produce 2 chunks, both having PageNumber = 1
        chunks.Count.Should().Be(2);
        chunks[0].PageNumber.Should().Be(1);
        chunks[1].PageNumber.Should().Be(1);
        chunks[0].Content.Should().Contain("Page/Section: Page 1");
        chunks[1].Content.Should().Contain("Page/Section: Page 1");
    }

    [Fact]
    public async Task ParseAsync_InfersEquipmentIdFromFileName()
    {
        var pdfBytes = CreateMinimalPdf("Equipment operating manual");
        var filePath = Path.Combine(_tempDir, "CONV01_Conveyor_Manual.pdf");
        await File.WriteAllBytesAsync(filePath, pdfBytes);

        var (document, chunks) = await _parser.ParseAsync(filePath, "Manuals/CONV01_Conveyor_Manual.pdf");

        document.EquipmentId.Should().Be("CONV01");
        chunks.Should().NotBeEmpty();
        chunks[0].EquipmentId.Should().Be("CONV01");
    }

    [Fact]
    public async Task ParseAsync_EmptyOrZeroBytePdf_ReturnsZeroChunks()
    {
        var filePath = Path.Combine(_tempDir, "empty.pdf");
        await File.WriteAllBytesAsync(filePath, Array.Empty<byte>());

        var (document, chunks) = await _parser.ParseAsync(filePath, "Manuals/empty.pdf");

        document.ChunkCount.Should().Be(0);
        chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseAsync_FileNotFound_ThrowsFileNotFoundException()
    {
        var act = () => _parser.ParseAsync(Path.Combine(_tempDir, "non_existent.pdf"), "non_existent.pdf");
        await act.Should().ThrowAsync<FileNotFoundException>();
    }
}
