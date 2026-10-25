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

public class EmpiricalM3StressTests : IDisposable
{
    private readonly string _tempDir;
    private readonly MarkdigYamlParser _markdigParser;
    private readonly PdfPigParser _pdfParser;
    private readonly PlainTextParser _plainTextParser;

    public EmpiricalM3StressTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "empirical_m3_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _markdigParser = new MarkdigYamlParser();
        _pdfParser = new PdfPigParser();
        _plainTextParser = new PlainTextParser();
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

    #region 1. MarkdigYamlParser Empirical Tests

    [Fact]
    public async Task Markdig_MalformedYaml_UnclosedQuotes_DoesNotCrashAndExtractsBody()
    {
        var content = @"---
equipment_id: ""ROBOT01
manufacturer: ABB
criticality: High
---
# Safety Notice
Ensure power is disconnected before servicing.";

        var filePath = Path.Combine(_tempDir, "unclosed_quotes.md");
        await File.WriteAllTextAsync(filePath, content);

        var (document, chunks) = await _markdigParser.ParseAsync(filePath, "Safety/unclosed_quotes.md");

        document.Should().NotBeNull();
        // Since YAML is malformed, frontmatter fields fail gracefully without crashing
        document.DocumentType.Should().Be(DocumentType.Markdown);
        document.Folder.Should().Be("Safety");
        chunks.Should().NotBeEmpty();
        chunks[0].Content.Should().Contain("Ensure power is disconnected before servicing.");
        chunks[0].SectionHeading.Should().Be("Safety Notice");
    }

    [Fact]
    public async Task Markdig_MalformedYaml_InvalidYamlSyntax_DoesNotCrash()
    {
        var content = @"---
equipment_id: ROBOT01
   invalid: [unbalanced brackets
description: :: bad colon mapping
---
# Alarm Procedure
Reset the circuit breaker.";

        var filePath = Path.Combine(_tempDir, "invalid_syntax.md");
        await File.WriteAllTextAsync(filePath, content);

        var (document, chunks) = await _markdigParser.ParseAsync(filePath, "Alarms/invalid_syntax.md");

        document.Should().NotBeNull();
        chunks.Should().NotBeEmpty();
        chunks[0].Content.Should().Contain("Reset the circuit breaker.");
    }

    [Fact]
    public async Task Markdig_NoFrontmatter_ParsesSuccessfully()
    {
        var content = @"# Direct Heading
This is a standard markdown document without any frontmatter headers.
It contains normal paragraphs and explanations.";

        var filePath = Path.Combine(_tempDir, "no_frontmatter.md");
        await File.WriteAllTextAsync(filePath, content);

        var (document, chunks) = await _markdigParser.ParseAsync(filePath, "Procedures/no_frontmatter.md");

        document.Should().NotBeNull();
        document.EquipmentId.Should().BeNull();
        document.Folder.Should().Be("Procedures");
        chunks.Should().ContainSingle();
        chunks[0].SectionHeading.Should().Be("Direct Heading");
        chunks[0].Content.Should().Contain("[Document: no_frontmatter.md | Folder: Procedures | Equipment: N/A | Fault/Alarm: N/A | Page/Section: Direct Heading]");
    }

    [Fact]
    public async Task Markdig_DuplicateTags_DeduplicatedAndMergedWithBodyInlineTags()
    {
        var content = @"---
equipment_id: ROBOT01
tags:
  - Robot
  - robot
  - ROBOT
  - #ROBOT
  - safety
---
# Main Section
Check the #robot and #SAFETY systems. Also inspect #pneumatics.";

        var filePath = Path.Combine(_tempDir, "duplicate_tags.md");
        await File.WriteAllTextAsync(filePath, content);

        var (document, chunks) = await _markdigParser.ParseAsync(filePath, "Robot/duplicate_tags.md");

        document.Tags.Should().BeEquivalentTo(new[] { "pneumatics", "robot", "safety" });
        // Ensure no uppercase or prefixed # tags
        document.Tags.Should().NotContain("#robot");
        document.Tags.Should().NotContain("ROBOT");
        chunks[0].Tags.Should().BeEquivalentTo(new[] { "pneumatics", "robot", "safety" });
    }

    [Fact]
    public async Task Markdig_ComplexNestedHeadings_MaintainsHeadingTitlesAndOrder()
    {
        var content = @"# Level 1 Heading
Content for Level 1.

### Level 3 Subheading
Content for Level 3 nested directly under Level 1.

## Level 2 Heading
Content for Level 2 appearing after Level 3.";

        var filePath = Path.Combine(_tempDir, "nested_headings.md");
        await File.WriteAllTextAsync(filePath, content);

        var (_, chunks) = await _markdigParser.ParseAsync(filePath, "Mechanical/nested_headings.md");

        chunks.Count.Should().Be(3);
        chunks[0].SectionHeading.Should().Be("Level 1 Heading");
        chunks[1].SectionHeading.Should().Be("Level 3 Subheading");
        chunks[2].SectionHeading.Should().Be("Level 2 Heading");

        chunks[0].Content.Should().Contain("Content for Level 1.");
        chunks[1].Content.Should().Contain("Content for Level 3 nested directly under Level 1.");
        chunks[2].Content.Should().Contain("Content for Level 2 appearing after Level 3.");
    }

    [Fact]
    public async Task Markdig_EmptySections_SkipsOrFormatsSafely()
    {
        var content = @"# Empty Section 1
# Empty Section 2
## Populated Section
This section has actual text.";

        var filePath = Path.Combine(_tempDir, "empty_sections.md");
        await File.WriteAllTextAsync(filePath, content);

        var (document, chunks) = await _markdigParser.ParseAsync(filePath, "Electrical/empty_sections.md");

        document.Should().NotBeNull();
        // Empty sections should not produce phantom crashes
        chunks.Should().NotBeEmpty();
        var populatedChunk = chunks.FirstOrDefault(c => c.SectionHeading == "Populated Section");
        populatedChunk.Should().NotBeNull();
        populatedChunk!.Content.Should().Contain("This section has actual text.");
    }

    [Fact]
    public async Task Markdig_WikilinksInsideHeadings_PreservedInHeadingAndBody()
    {
        var content = @"---
equipment_id: ROBOT01
---
# Guide to [[ROBOT01]] Controller
Refer to [[IRB4600 Manual]] and [[Safety Procedures]].";

        var filePath = Path.Combine(_tempDir, "wikilink_heading.md");
        await File.WriteAllTextAsync(filePath, content);

        var (_, chunks) = await _markdigParser.ParseAsync(filePath, "Robot/wikilink_heading.md");

        chunks.Should().ContainSingle();
        chunks[0].SectionHeading.Should().Be("Guide to [[ROBOT01]] Controller");
        chunks[0].Content.Should().Contain("[[ROBOT01]]");
        chunks[0].Content.Should().Contain("[[IRB4600 Manual]]");
        chunks[0].Content.Should().Contain("[[Safety Procedures]]");
    }

    [Fact]
    public async Task Markdig_PreambleBeforeFirstHeading_ChunkedWithNullHeading()
    {
        var content = @"---
equipment_id: CONV01
---
This is preamble text before any section heading appears in the document.

# First Actual Heading
Content of the first heading.";

        var filePath = Path.Combine(_tempDir, "preamble.md");
        await File.WriteAllTextAsync(filePath, content);

        var (_, chunks) = await _markdigParser.ParseAsync(filePath, "Pneumatics/preamble.md");

        chunks.Count.Should().Be(2);
        chunks[0].SectionHeading.Should().BeNull();
        chunks[0].Content.Should().Contain("This is preamble text before any section heading");
        chunks[0].Content.Should().Contain("Page/Section: N/A");

        chunks[1].SectionHeading.Should().Be("First Actual Heading");
        chunks[1].Content.Should().Contain("Content of the first heading.");
        chunks[1].Content.Should().Contain("Page/Section: First Actual Heading");
    }

    #endregion

    #region 2. ChunkContextEnricher Empirical Tests

    [Theory]
    [InlineData("folder//subfolder/file.md", "folder//subfolder/file.md", "folder")]
    [InlineData(@"\windows\path\file.md", "windows/path/file.md", "windows")]
    [InlineData(@"windows\sub\file.md", "windows/sub/file.md", "windows")]
    [InlineData("/Alarms/ALM-205.md", "Alarms/ALM-205.md", "Alarms")]
    [InlineData("///deep///folder/file.md", "deep///folder/file.md", "deep")]
    [InlineData("RootFile.md", "RootFile.md", "General")]
    [InlineData("", "", "General")]
    [InlineData(null, "", "General")]
    public void Enricher_PathNormalization_HandlesStrangePaths(string? input, string expectedNormalized, string expectedFolder)
    {
        var normalized = ChunkContextEnricher.NormalizeRelativePath(input);
        normalized.Should().Be(expectedNormalized);

        var folder = ChunkContextEnricher.ExtractFolder(input);
        folder.Should().Be(expectedFolder);
    }

    [Fact]
    public void Enricher_FormatHeader_NullOrMissingFields_ProducesExactVerbatimFormat()
    {
        var header = ChunkContextEnricher.FormatHeader(
            fileName: null!,
            folder: null!,
            equipmentId: null,
            faultOrAlarmId: null,
            pageOrHeading: null);

        header.Should().Be("[Document: Document | Folder: General | Equipment: N/A | Fault/Alarm: N/A | Page/Section: N/A]");
    }

    [Fact]
    public void Enricher_FormatHeader_WhitespaceFields_TrimsAndDefaults()
    {
        var header = ChunkContextEnricher.FormatHeader(
            fileName: "  MyDoc.md  ",
            folder: "  Faults  ",
            equipmentId: "  ROBOT01  ",
            faultOrAlarmId: "  FLT-001  ",
            pageOrHeading: "  Section A  ");

        header.Should().Be("[Document: MyDoc.md | Folder: Faults | Equipment: ROBOT01 | Fault/Alarm: FLT-001 | Page/Section: Section A]");
    }

    [Fact]
    public void Enricher_SlidingWindow_EmptyText_ReturnsEmptyArray()
    {
        var chunks1 = ChunkContextEnricher.ChunkWords("");
        var chunks2 = ChunkContextEnricher.ChunkWords("    \t\r\n   ");
        var chunks3 = ChunkContextEnricher.ChunkWords(null!);

        chunks1.Should().BeEmpty();
        chunks2.Should().BeEmpty();
        chunks3.Should().BeEmpty();
    }

    [Fact]
    public void Enricher_SlidingWindow_SingleWord_ReturnsSingleChunk()
    {
        var chunks = ChunkContextEnricher.ChunkWords("SingleWord");

        chunks.Should().ContainSingle();
        chunks[0].Should().Be("SingleWord");
    }

    [Fact]
    public void Enricher_SlidingWindow_499Words_ReturnsSingleChunk()
    {
        var text = string.Join(" ", Enumerable.Range(1, 499).Select(i => $"word{i}"));
        var chunks = ChunkContextEnricher.ChunkWords(text);

        chunks.Should().ContainSingle();
        chunks[0].Split(' ').Length.Should().Be(499);
    }

    [Fact]
    public void Enricher_SlidingWindow_500Words_ReturnsSingleChunk()
    {
        var text = string.Join(" ", Enumerable.Range(1, 500).Select(i => $"word{i}"));
        var chunks = ChunkContextEnricher.ChunkWords(text);

        chunks.Should().ContainSingle();
        chunks[0].Split(' ').Length.Should().Be(500);
    }

    [Fact]
    public void Enricher_SlidingWindow_501Words_ReturnsTwoChunksWithExact100Overlap()
    {
        var text = string.Join(" ", Enumerable.Range(1, 501).Select(i => $"word{i}"));
        var chunks = ChunkContextEnricher.ChunkWords(text);

        chunks.Count.Should().Be(2);

        var firstChunkWords = chunks[0].Split(' ');
        var secondChunkWords = chunks[1].Split(' ');

        firstChunkWords.Length.Should().Be(500); // 1..500
        secondChunkWords.Length.Should().Be(101); // 401..501

        firstChunkWords.First().Should().Be("word1");
        firstChunkWords.Last().Should().Be("word500");

        secondChunkWords.First().Should().Be("word401");
        secondChunkWords.Last().Should().Be("word501");

        // Overlap verification: exactly 100 words (word401..word500)
        var overlap = firstChunkWords.Intersect(secondChunkWords).ToList();
        overlap.Count.Should().Be(100);
        overlap.First().Should().Be("word401");
        overlap.Last().Should().Be("word500");
    }

    [Fact]
    public void Enricher_SlidingWindow_1000Words_ReturnsThreeChunksWithExactOverlaps()
    {
        var text = string.Join(" ", Enumerable.Range(1, 1000).Select(i => $"word{i}"));
        var chunks = ChunkContextEnricher.ChunkWords(text);

        chunks.Count.Should().Be(3);

        var c0 = chunks[0].Split(' ');
        var c1 = chunks[1].Split(' ');
        var c2 = chunks[2].Split(' ');

        c0.Length.Should().Be(500); // 1..500
        c1.Length.Should().Be(500); // 401..900
        c2.Length.Should().Be(200); // 801..1000

        c0.First().Should().Be("word1");
        c0.Last().Should().Be("word500");

        c1.First().Should().Be("word401");
        c1.Last().Should().Be("word900");

        c2.First().Should().Be("word801");
        c2.Last().Should().Be("word1000");

        // Overlap between c0 and c1: 100 words (401..500)
        c0.Intersect(c1).Count().Should().Be(100);

        // Overlap between c1 and c2: 100 words (801..900)
        c1.Intersect(c2).Count().Should().Be(100);
    }

    #endregion

    #region 3. PdfPigParser and PlainTextParser Empirical Tests

    [Fact]
    public async Task PdfPig_CorruptedOrInvalidPdf_DoesNotThrowAndReturnsZeroChunks()
    {
        var filePath = Path.Combine(_tempDir, "corrupted.pdf");
        await File.WriteAllTextAsync(filePath, "Not a real PDF file content");

        var (document, chunks) = await _pdfParser.ParseAsync(filePath, "Manuals/corrupted.pdf");

        document.Should().NotBeNull();
        document.ChunkCount.Should().Be(0);
        chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task PdfPig_MultiPageWithEquipmentInference_Sets1BasedPageNumbersAndEquipmentId()
    {
        var page1 = "Page 1: Overview of the PLC01 controller architecture.";
        var page2 = "Page 2: Wiring schematic for digital input modules.";
        var page3 = "Page 3: Troubleshooting fault codes and communications.";

        var pdfBytes = CreateMinimalPdf(page1, page2, page3);
        var filePath = Path.Combine(_tempDir, "PLC01_Manual_v2.pdf");
        await File.WriteAllBytesAsync(filePath, pdfBytes);

        var (document, chunks) = await _pdfParser.ParseAsync(filePath, "Manuals/PLC01_Manual_v2.pdf");

        document.EquipmentId.Should().Be("PLC01");
        document.DocumentType.Should().Be(DocumentType.Pdf);
        document.Folder.Should().Be("Manuals");

        chunks.Count.Should().Be(3);
        chunks[0].PageNumber.Should().Be(1);
        chunks[1].PageNumber.Should().Be(2);
        chunks[2].PageNumber.Should().Be(3);

        chunks[0].EquipmentId.Should().Be("PLC01");
        chunks[1].EquipmentId.Should().Be("PLC01");
        chunks[2].EquipmentId.Should().Be("PLC01");

        chunks[0].Content.Should().Contain("Page/Section: Page 1");
        chunks[1].Content.Should().Contain("Page/Section: Page 2");
        chunks[2].Content.Should().Contain("Page/Section: Page 3");
    }

    [Fact]
    public async Task PlainText_SlidingWindow501Words_ChunksAndMaintainsZeroBasedIndices()
    {
        var words = string.Join(" ", Enumerable.Range(1, 501).Select(i => $"logEntry{i}"));
        var filePath = Path.Combine(_tempDir, "large_log.txt");
        await File.WriteAllTextAsync(filePath, words);

        var (document, chunks) = await _plainTextParser.ParseAsync(filePath, "Maintenance/large_log.txt");

        document.DocumentType.Should().Be(DocumentType.Text);
        document.Folder.Should().Be("Maintenance");
        document.ChunkCount.Should().Be(2);

        chunks.Count.Should().Be(2);
        chunks[0].ChunkIndex.Should().Be(0);
        chunks[1].ChunkIndex.Should().Be(1);

        chunks[0].PageNumber.Should().BeNull();
        chunks[0].SectionHeading.Should().BeNull();
        chunks[1].PageNumber.Should().BeNull();
        chunks[1].SectionHeading.Should().BeNull();

        chunks[0].Content.Should().StartWith("[Document: large_log.txt | Folder: Maintenance | Equipment: N/A | Fault/Alarm: N/A | Page/Section: N/A]\n");
        chunks[1].Content.Should().StartWith("[Document: large_log.txt | Folder: Maintenance | Equipment: N/A | Fault/Alarm: N/A | Page/Section: N/A]\n");
    }

    [Fact]
    public async Task PlainText_EmptyOrWhitespace_ReturnsZeroChunks()
    {
        var filePath = Path.Combine(_tempDir, "whitespace.txt");
        await File.WriteAllTextAsync(filePath, "   \n\t   \r\n   ");

        var (document, chunks) = await _plainTextParser.ParseAsync(filePath, "Maintenance/whitespace.txt");

        document.ChunkCount.Should().Be(0);
        chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task Markdig_UnclosedFrontmatterDelimiter_HandledSafely()
    {
        var content = @"---
equipment_id: ROBOT01
No closing delimiter here
# Heading 1
Some text.";

        var filePath = Path.Combine(_tempDir, "unclosed_frontmatter.md");
        await File.WriteAllTextAsync(filePath, content);

        var (document, chunks) = await _markdigParser.ParseAsync(filePath, "Robot/unclosed_frontmatter.md");

        document.Should().NotBeNull();
        chunks.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Markdig_FrontmatterOnlyNoBody_ReturnsZeroChunks()
    {
        var content = @"---
equipment_id: ROBOT01
manufacturer: ABB
criticality: Low
---
";

        var filePath = Path.Combine(_tempDir, "frontmatter_only.md");
        await File.WriteAllTextAsync(filePath, content);

        var (document, chunks) = await _markdigParser.ParseAsync(filePath, "Equipment/frontmatter_only.md");

        document.EquipmentId.Should().Be("ROBOT01");
        document.ChunkCount.Should().Be(0);
        chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task Markdig_CompletelyEmptyFile_ReturnsZeroChunks()
    {
        var filePath = Path.Combine(_tempDir, "empty.md");
        await File.WriteAllTextAsync(filePath, "");

        var (document, chunks) = await _markdigParser.ParseAsync(filePath, "General/empty.md");

        document.Should().NotBeNull();
        document.ChunkCount.Should().Be(0);
        chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task Markdig_DuplicateKeysInYaml_HandlesGracefully()
    {
        var content = @"---
equipment_id: ROBOT01
equipment_id: ROBOT02
manufacturer: ABB
---
# Main
Some body text.";

        var filePath = Path.Combine(_tempDir, "duplicate_keys.md");
        await File.WriteAllTextAsync(filePath, content);

        var (document, chunks) = await _markdigParser.ParseAsync(filePath, "Equipment/duplicate_keys.md");

        document.Should().NotBeNull();
        chunks.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Markdig_HeadingWithSpecialCharsAndEmoji_ExtractsCorrectHeading()
    {
        var content = @"# ⚠️ Alarm ALM-205 (Gripper Timeout)
Immediate shutdown required.";

        var filePath = Path.Combine(_tempDir, "emoji_heading.md");
        await File.WriteAllTextAsync(filePath, content);

        var (_, chunks) = await _markdigParser.ParseAsync(filePath, "Alarms/emoji_heading.md");

        chunks.Should().ContainSingle();
        chunks[0].SectionHeading.Should().Be("⚠️ Alarm ALM-205 (Gripper Timeout)");
        chunks[0].Content.Should().Contain("Page/Section: ⚠️ Alarm ALM-205 (Gripper Timeout)");
    }

    [Fact]
    public async Task Markdig_HeadingLevelsBeyond3_AreNotSplit()
    {
        var content = @"# Level 1 Heading
Content 1.

#### Level 4 Subheading
Content 4 is part of Level 1.";

        var filePath = Path.Combine(_tempDir, "level4.md");
        await File.WriteAllTextAsync(filePath, content);

        var (_, chunks) = await _markdigParser.ParseAsync(filePath, "Notes/level4.md");

        // Level 4 should not start a new chunk section
        chunks.Should().ContainSingle();
        chunks[0].SectionHeading.Should().Be("Level 1 Heading");
        chunks[0].Content.Should().Contain("#### Level 4 Subheading");
    }

    [Fact]
    public async Task Markdig_MassiveSection_4000Words_PreservesAllWords()
    {
        var words = string.Join(" ", Enumerable.Range(1, 4000).Select(i => $"token{i}"));
        var content = $@"# Massive Section
{words}";

        var filePath = Path.Combine(_tempDir, "massive.md");
        await File.WriteAllTextAsync(filePath, content);

        var (_, chunks) = await _markdigParser.ParseAsync(filePath, "Manuals/massive.md");

        // 4000 words: 500w window, 400w step.
        // Chunk 0: 0..499 (500)
        // Chunk 1: 400..899 (500)
        // Chunk 2: 800..1299 (500)
        // Chunk 3: 1200..1699 (500)
        // Chunk 4: 1600..2099 (500)
        // Chunk 5: 2000..2499 (500)
        // Chunk 6: 2400..2899 (500)
        // Chunk 7: 2800..3299 (500)
        // Chunk 8: 3200..3699 (500)
        // Chunk 9: 3600..3999 (400)
        // Total = 10 chunks
        chunks.Count.Should().Be(10);
        chunks.First().Content.Should().Contain("token1");
        chunks.Last().Content.Should().Contain("token4000");
    }

    [Fact]
    public void Enricher_Sha256_KnownVectors_ProducesAccurateHashes()
    {
        var emptyBytes = Array.Empty<byte>();
        ChunkContextEnricher.ComputeSha256(emptyBytes)
            .Should().Be("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");

        var testBytes = System.Text.Encoding.UTF8.GetBytes("The quick brown fox jumps over the lazy dog");
        ChunkContextEnricher.ComputeSha256(testBytes)
            .Should().Be("d7a8fbb307d7809469ca9abcb0082e4f8d5651e46d3cdb762d02d0bf37c9e592");
    }

    [Fact]
    public void Enricher_ChunkWords_PreservesOriginalFormattingWhenUnderLimit()
    {
        var text = "  word1   \t\n  word2  \r\n\t  word3   ";
        var chunks = ChunkContextEnricher.ChunkWords(text);

        // When text <= 500 words, original text is preserved without mangling internal newlines
        chunks.Should().ContainSingle();
        chunks[0].Should().Be(text.Trim());
    }

    [Fact]
    public void Enricher_ChunkWords_OverlapLargerThanMaxWords_DoesNotHang()
    {
        var text = string.Join(" ", Enumerable.Range(1, 50).Select(i => $"w{i}"));
        // maxWords = 10, overlapWords = 20 -> step should become maxWords (10)
        var chunks = ChunkContextEnricher.ChunkWords(text, maxWords: 10, overlapWords: 20);

        chunks.Count.Should().Be(5);
    }

    [Fact]
    public async Task PdfPig_CorruptedBinaryAfterHeader_CatchesAndReturnsZeroChunks()
    {
        var corruptedBytes = new byte[] { (byte)'%', (byte)'P', (byte)'D', (byte)'F', 0x00, 0xFF, 0xFE, 0xFD, 0x12, 0x34 };
        var filePath = Path.Combine(_tempDir, "corrupted_stream.pdf");
        await File.WriteAllBytesAsync(filePath, corruptedBytes);

        var (document, chunks) = await _pdfParser.ParseAsync(filePath, "Manuals/corrupted_stream.pdf");

        document.Should().NotBeNull();
        document.ChunkCount.Should().Be(0);
        chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task PdfPig_PagesWithOnlyWhitespace_AreSkipped()
    {
        var page1 = "Page 1 has valid text.";
        var page2 = "          ";
        var page3 = "Page 3 has valid text.";

        var pdfBytes = CreateMinimalPdf(page1, page2, page3);
        var filePath = Path.Combine(_tempDir, "whitespace_pages.pdf");
        await File.WriteAllBytesAsync(filePath, pdfBytes);

        var (document, chunks) = await _pdfParser.ParseAsync(filePath, "Manuals/whitespace_pages.pdf");

        // Page 2 should be skipped, leaving 2 chunks
        chunks.Count.Should().Be(2);
        chunks[0].PageNumber.Should().Be(1);
        chunks[1].PageNumber.Should().Be(3);
    }

    [Fact]
    public async Task PdfPig_CaseInsensitiveEquipmentInference_MatchesLowercase()
    {
        var pdfBytes = CreateMinimalPdf("Equipment guide.");
        var filePath = Path.Combine(_tempDir, "robot01_installation.pdf");
        await File.WriteAllBytesAsync(filePath, pdfBytes);

        var (document, chunks) = await _pdfParser.ParseAsync(filePath, "Manuals/robot01_installation.pdf");

        document.EquipmentId.Should().Be("ROBOT01");
        chunks[0].EquipmentId.Should().Be("ROBOT01");
    }

    #endregion
}
