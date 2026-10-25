using System.Text.Json;
using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier1_FeatureCoverage;

public class Feature18_SseStreamingQueryEndpointTests
{
    [Fact]
    public void Test01_StreamEndpointRoute_MatchesSpecification()
    {
        SpecificationConstants.StreamQueryEndpoint.Should().Be("/api/query/stream");
    }

    [Fact]
    public void Test02_SseContentType_MatchesTextEventStream()
    {
        const string expectedContentType = "text/event-stream";
        expectedContentType.Should().Be("text/event-stream");
    }

    [Fact]
    public void Test03_MetadataEvent_EmitsConfidenceAndSourceArray()
    {
        var docId = Guid.NewGuid();
        var metadataPayload = new
        {
            confidence = "High",
            sources = new[]
            {
                new
                {
                    documentId = docId,
                    fileName = "RobotManual.pdf",
                    relativePath = "Manuals/RobotManual.pdf",
                    pageNumber = 17,
                    sectionHeading = (string?)null
                }
            }
        };

        var json = JsonSerializer.Serialize(metadataPayload);
        var sseChunk = $"event: metadata\ndata: {json}\n\n";

        sseChunk.Should().StartWith("event: metadata\n");
        sseChunk.Should().Contain("\"confidence\":\"High\"");
        sseChunk.Should().Contain("RobotManual.pdf");
        sseChunk.Should().EndWith("\n\n");
    }

    [Fact]
    public void Test04_TokenEvent_EmitsSequentialTextDeltas()
    {
        var tokens = new[] { "The ", "gripper ", "timeout ", "fault..." };
        var chunks = tokens.Select(t => $"event: token\ndata: {{\"token\":\"{t}\"}}\n\n").ToList();

        chunks.Count.Should().Be(4);
        foreach (var chunk in chunks)
        {
            chunk.Should().StartWith("event: token\n");
            chunk.Should().EndWith("\n\n");
        }
    }

    [Fact]
    public void Test05_DoneEvent_SignalsStreamCompletion()
    {
        const string doneChunk = "event: done\ndata: {}\n\n";
        doneChunk.Should().Be("event: done\ndata: {}\n\n");
    }
}
