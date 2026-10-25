namespace CellAssistant.UnitTests.Parsing;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CellAssistant.Domain.Enums;
using CellAssistant.Infrastructure.Parsing;
using FluentAssertions;
using Xunit;

public class PlainTextParserTests : IDisposable
{
    private readonly string _tempDir;
    private readonly PlainTextParser _parser;

    public PlainTextParserTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "plaintext_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _parser = new PlainTextParser();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Theory]
    [InlineData("log.txt", true)]
    [InlineData("LOG.TXT", true)]
    [InlineData("note.md", false)]
    [InlineData("manual.pdf", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void CanParse_ChecksExtensionCorrectly(string? path, bool expected)
    {
        _parser.CanParse(path!).Should().Be(expected);
    }

    [Fact]
    public async Task ParseAsync_SmallText_ProducesSingleChunk()
    {
        var content = "This is a brief maintenance note from shift 1.";
        var filePath = Path.Combine(_tempDir, "brief.txt");
        await File.WriteAllTextAsync(filePath, content);

        var (document, chunks) = await _parser.ParseAsync(filePath, "Maintenance/brief.txt");

        document.DocumentType.Should().Be(DocumentType.Text);
        document.Folder.Should().Be("Maintenance");
        chunks.Should().ContainSingle();
        chunks[0].ChunkIndex.Should().Be(0);
        chunks[0].PageNumber.Should().BeNull();
        chunks[0].SectionHeading.Should().BeNull();
        chunks[0].Content.Should().Contain(content);
    }

    [Fact]
    public async Task ParseAsync_OversizedText_ProducesSlidingWindowOverlap()
    {
        var words = string.Join(" ", Enumerable.Range(1, 1200).Select(i => $"log{i}"));
        var filePath = Path.Combine(_tempDir, "oversized.txt");
        await File.WriteAllTextAsync(filePath, words);

        var (document, chunks) = await _parser.ParseAsync(filePath, "Maintenance/oversized.txt");

        // 1200 words with 500w window and 100w overlap produces 3 chunks
        chunks.Count.Should().Be(3);
        chunks[0].ChunkIndex.Should().Be(0);
        chunks[1].ChunkIndex.Should().Be(1);
        chunks[2].ChunkIndex.Should().Be(2);

        document.ChunkCount.Should().Be(3);
    }

    [Fact]
    public async Task ParseAsync_ContextHeaders_ContainsNaDefaults()
    {
        var content = "Some standard sensor readings.";
        var filePath = Path.Combine(_tempDir, "readings.txt");
        await File.WriteAllTextAsync(filePath, content);

        var (_, chunks) = await _parser.ParseAsync(filePath, "Maintenance/readings.txt");

        chunks.Should().ContainSingle();
        chunks[0].Content.Should().StartWith("[Document: readings.txt | Folder: Maintenance | Equipment: N/A | Fault/Alarm: N/A | Page/Section: N/A]\n");
    }

    [Fact]
    public async Task ParseAsync_EmptyTextFile_ReturnsZeroChunks()
    {
        var filePath = Path.Combine(_tempDir, "empty.txt");
        await File.WriteAllTextAsync(filePath, "");

        var (document, chunks) = await _parser.ParseAsync(filePath, "Maintenance/empty.txt");

        document.ChunkCount.Should().Be(0);
        chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseAsync_FileNotFound_ThrowsFileNotFoundException()
    {
        var act = () => _parser.ParseAsync(Path.Combine(_tempDir, "non_existent.txt"), "non_existent.txt");
        await act.Should().ThrowAsync<FileNotFoundException>();
    }
}
