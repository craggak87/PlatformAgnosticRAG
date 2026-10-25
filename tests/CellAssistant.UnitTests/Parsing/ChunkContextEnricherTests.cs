namespace CellAssistant.UnitTests.Parsing;

using System;
using System.Linq;
using System.Text;
using CellAssistant.Infrastructure.Parsing;
using FluentAssertions;
using Xunit;

public class ChunkContextEnricherTests
{
    [Fact]
    public void FormatHeader_AllFieldsProvided_ProducesExactBracketedTemplate()
    {
        var header = ChunkContextEnricher.FormatHeader(
            fileName: "Robot.md",
            folder: "Faults",
            equipmentId: "ROBOT01",
            faultOrAlarmId: "FLT-001",
            pageOrHeading: "Symptoms");

        header.Should().Be("[Document: Robot.md | Folder: Faults | Equipment: ROBOT01 | Fault/Alarm: FLT-001 | Page/Section: Symptoms]");
    }

    [Fact]
    public void FormatHeader_MissingFields_SubstitutesWithNA()
    {
        var header = ChunkContextEnricher.FormatHeader(
            fileName: "Doc.txt",
            folder: "General",
            equipmentId: null,
            faultOrAlarmId: null,
            pageOrHeading: null);

        header.Should().Be("[Document: Doc.txt | Folder: General | Equipment: N/A | Fault/Alarm: N/A | Page/Section: N/A]");
    }

    [Fact]
    public void FormatHeader_WhitespaceFields_SubstitutesWithNA()
    {
        var header = ChunkContextEnricher.FormatHeader(
            fileName: "",
            folder: "   ",
            equipmentId: "  ",
            faultOrAlarmId: "\t",
            pageOrHeading: "\n");

        header.Should().Be("[Document: Document | Folder: General | Equipment: N/A | Fault/Alarm: N/A | Page/Section: N/A]");
    }

    [Fact]
    public void EnrichChunk_AppendsContentWithSingleNewline()
    {
        var header = "[Document: Doc.txt | Folder: General | Equipment: N/A | Fault/Alarm: N/A | Page/Section: N/A]";
        var content = "This is the body content.";

        var enriched = ChunkContextEnricher.EnrichChunk(header, content);

        enriched.Should().Be($"{header}\n{content}");
    }

    [Fact]
    public void NormalizeRelativePath_WindowsBackslashes_ConvertedToPosix()
    {
        var path = @"Faults\Sub\Robot.md";
        var normalized = ChunkContextEnricher.NormalizeRelativePath(path);

        normalized.Should().Be("Faults/Sub/Robot.md");
    }

    [Fact]
    public void NormalizeRelativePath_LeadingSlash_Trimmed()
    {
        var path = "/Alarms/ALM-205.md";
        var normalized = ChunkContextEnricher.NormalizeRelativePath(path);

        normalized.Should().Be("Alarms/ALM-205.md");
    }

    [Fact]
    public void ExtractFolder_NestedRelativePath_ReturnsFirstSegment()
    {
        var path = "Equipment/PLC/Siemens.md";
        var folder = ChunkContextEnricher.ExtractFolder(path);

        folder.Should().Be("Equipment");
    }

    [Fact]
    public void ExtractFolder_RootFile_ReturnsDefaultFolder()
    {
        var path = "README.md";
        var folder = ChunkContextEnricher.ExtractFolder(path, "Root");

        folder.Should().Be("Root");
    }

    [Fact]
    public void ChunkWords_SlidingWindow_500Max100Overlap()
    {
        // 1. Text with 450 words -> 1 chunk
        var words450 = string.Join(" ", Enumerable.Range(1, 450).Select(i => $"word{i}"));
        var chunks450 = ChunkContextEnricher.ChunkWords(words450, 500, 100);
        chunks450.Count.Should().Be(1);

        // 2. Text with 500 words -> 1 chunk
        var words500 = string.Join(" ", Enumerable.Range(1, 500).Select(i => $"word{i}"));
        var chunks500 = ChunkContextEnricher.ChunkWords(words500, 500, 100);
        chunks500.Count.Should().Be(1);

        // 3. Text with 600 words -> 2 chunks
        var words600 = string.Join(" ", Enumerable.Range(1, 600).Select(i => $"word{i}"));
        var chunks600 = ChunkContextEnricher.ChunkWords(words600, 500, 100);
        chunks600.Count.Should().Be(2);

        var firstWords = chunks600[0].Split(' ');
        var secondWords = chunks600[1].Split(' ');
        firstWords.Length.Should().Be(500);
        secondWords.Length.Should().Be(200);

        // Check overlap is exactly 100 words (word401..word500)
        var overlap = firstWords.Intersect(secondWords).ToList();
        overlap.Count.Should().Be(100);

        // 4. Text with 1200 words -> 3 chunks
        var words1200 = string.Join(" ", Enumerable.Range(1, 1200).Select(i => $"word{i}"));
        var chunks1200 = ChunkContextEnricher.ChunkWords(words1200, 500, 100);
        chunks1200.Count.Should().Be(3);
    }

    [Fact]
    public void ComputeSha256_EmptyBytes_ProducesNistKnownHash()
    {
        var emptyBytes = Array.Empty<byte>();
        var hash = ChunkContextEnricher.ComputeSha256(emptyBytes);

        hash.Should().Be("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    [Fact]
    public void ComputeSha256_NullBytes_ThrowsArgumentNullException()
    {
        var act = () => ChunkContextEnricher.ComputeSha256(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
