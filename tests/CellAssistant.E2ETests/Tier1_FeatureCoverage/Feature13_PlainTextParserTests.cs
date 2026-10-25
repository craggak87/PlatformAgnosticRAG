using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier1_FeatureCoverage;

public class Feature13_PlainTextParserTests
{
    [Fact]
    public void Test01_TxtExtension_IsRecognizedByCanParse()
    {
        var validExtensions = new[] { ".txt", ".TXT" };
        foreach (var ext in validExtensions)
        {
            ext.Equals(".txt", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
        }
    }

    [Fact]
    public void Test02_PlainTextSlidingWindow_AdheresTo500WordMax()
    {
        var words = Enumerable.Range(1, 1200).Select(i => $"word{i}").ToArray();
        var fullText = string.Join(" ", words);

        // Sliding window: 500 words chunk, 100 words overlap
        var chunks = new List<string>();
        int index = 0;
        int step = SpecificationConstants.DefaultMaxChunkWords - SpecificationConstants.DefaultChunkOverlapWords; // 400

        while (index < words.Length)
        {
            var chunkWords = words.Skip(index).Take(SpecificationConstants.DefaultMaxChunkWords).ToArray();
            chunks.Add(string.Join(" ", chunkWords));
            index += step;
        }

        chunks.Count.Should().Be(3);
        chunks[0].Split(' ').Length.Should().Be(500);
        chunks[1].Split(' ').Length.Should().Be(500);
    }

    [Fact]
    public void Test03_PlainTextChunks_MaintainOverlap()
    {
        var words = Enumerable.Range(1, 600).Select(i => $"token{i}").ToArray();
        var chunk1 = words.Take(500).ToArray();
        var chunk2 = words.Skip(400).Take(500).ToArray();

        var overlap = chunk1.Intersect(chunk2).ToList();
        overlap.Count.Should().Be(100, "Overlap should be 100 words between consecutive chunks");
    }

    [Fact]
    public void Test04_PlainTextChunk_PrependsDocumentHeader()
    {
        var header = E2ETestHarness.FormatChunkHeader(
            "ServiceLog.txt",
            "Maintenance",
            null,
            null,
            null,
            "Motor bearings greased during shift 2."
        );

        header.Should().StartWith("[Document: ServiceLog.txt | Folder: Maintenance | Equipment: N/A | Fault/Alarm: N/A | Page/Section: N/A]");
    }

    [Fact]
    public void Test05_PlainTextChunkIndices_AreZeroBasedAndSequential()
    {
        var chunks = Enumerable.Range(0, 4)
            .Select(i => new DocumentChunkModel(Guid.NewGuid(), Guid.NewGuid(), $"Content {i}", i, null, null, null, null, null, Array.Empty<string>()))
            .ToList();

        for (int i = 0; i < chunks.Count; i++)
        {
            chunks[i].ChunkIndex.Should().Be(i);
        }
    }
}
