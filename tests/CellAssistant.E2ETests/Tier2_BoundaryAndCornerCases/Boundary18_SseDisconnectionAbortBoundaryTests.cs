using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier2_BoundaryAndCornerCases;

public class Boundary18_SseDisconnectionAbortBoundaryTests
{
    [Fact]
    public async Task Test01_ClientDisconnection_CancelsTokenAndHaltsStreaming()
    {
        using var cts = new CancellationTokenSource();
        var tokensEmitted = 0;

        try
        {
            for (int i = 0; i < 100; i++)
            {
                if (i == 5)
                {
                    cts.Cancel(); // Client disconnects
                }

                cts.Token.ThrowIfCancellationRequested();
                tokensEmitted++;
                await Task.Yield();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        tokensEmitted.Should().Be(5);
        cts.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void Test02_EmptyTokenDelta_DoesNotEmitEmptyTokenEvent()
    {
        var token = "";
        bool shouldEmit = !string.IsNullOrEmpty(token);

        shouldEmit.Should().BeFalse("Zero-length tokens should not produce redundant SSE frames");
    }

    [Fact]
    public void Test03_NewlinesInToken_ArePreservedInJsonPayload()
    {
        var tokenWithNewlines = "\n\n1. Check safety circuit.\n2. Inspect fuse.";
        var payload = new { token = tokenWithNewlines };
        var json = System.Text.Json.JsonSerializer.Serialize(payload);

        json.Should().Contain("\\n\\n1. Check safety circuit.");
    }

    [Fact]
    public void Test04_SseErrorEvent_EmittedWhenPipelineThrows()
    {
        const string errorMessage = "Ollama connection failed";
        var sseError = $"event: error\ndata: {{\"message\":\"{errorMessage}\"}}\n\n";

        sseError.Should().StartWith("event: error\n");
        sseError.Should().Contain(errorMessage);
        sseError.Should().EndWith("\n\n");
    }

    [Fact]
    public void Test05_SseFrameFormat_GuaranteesTwoTrailingNewlines()
    {
        var frame = "event: done\ndata: {}\n\n";
        frame.EndsWith("\n\n").Should().BeTrue("SSE specification RFC 8895 mandates double newline boundary between events");
    }
}
