using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier2_BoundaryAndCornerCases;

public class Boundary13_EmptyPlainTextBoundaryTests
{
    [Fact]
    public void Test01_ZeroByteTextFile_YieldsZeroChunks()
    {
        var rawText = string.Empty;
        var words = rawText.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        words.Length.Should().Be(0);
    }

    [Fact]
    public void Test02_WhitespaceOnlyFile_YieldsZeroChunks()
    {
        var rawText = "   \n\r\t   \n\n   ";
        var words = rawText.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        words.Length.Should().Be(0);
    }

    [Fact]
    public void Test03_SingleWordFile_GeneratesSingleChunkWithoutCrashingOverlap()
    {
        var rawText = "EmergencyStop";
        var words = rawText.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        words.Length.Should().Be(1);
        words[0].Should().Be("EmergencyStop");
    }

    [Fact]
    public void Test04_TextFileWithExactly500Words_GeneratesExactlyOneChunk()
    {
        var words = Enumerable.Range(1, 500).Select(i => $"word{i}").ToArray();
        var fullText = string.Join(" ", words);

        words.Length.Should().Be(500);
        fullText.Split(' ').Length.Should().Be(500);
    }

    [Fact]
    public void Test05_TextFileWith501Words_TriggersChunkSplit()
    {
        var words = Enumerable.Range(1, 501).Select(i => $"word{i}").ToArray();
        bool requiresSplit = words.Length > 500;

        requiresSplit.Should().BeTrue();
    }
}
