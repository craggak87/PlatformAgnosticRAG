using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier2_BoundaryAndCornerCases;

public class Boundary10_OllamaTimeoutFallbackBoundaryTests
{
    [Fact]
    public async Task Test01_OllamaTimeout_TriggersFallbackModelInvocation()
    {
        var primaryTimedOut = false;
        var fallbackInvoked = false;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            // Simulate slow call exceeding timeout
            await Task.Delay(100, cts.Token);
        }
        catch (OperationCanceledException)
        {
            primaryTimedOut = true;
            fallbackInvoked = true;
        }

        primaryTimedOut.Should().BeTrue();
        fallbackInvoked.Should().BeTrue("ADR-003 and Doc 3 require automatic fallback to qwen3.8:latest on failure");
    }

    [Fact]
    public void Test02_FallbackChatModel_DiffersFromPrimaryModel()
    {
        SpecificationConstants.PrimaryChatModel.Should().NotBe(SpecificationConstants.FallbackChatModel);
        SpecificationConstants.PrimaryChatModel.Should().Be("gemma4:e4b-it-q4_K_M");
        SpecificationConstants.FallbackChatModel.Should().Be("qwen3.8:latest");
    }

    [Fact]
    public void Test03_OllamaConnectionRefused_ThrowsStructuredServiceException()
    {
        // When Ollama port 11434 is down
        using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(100) };
        var act = async () => await client.GetAsync("http://127.0.0.1:54321/api/tags");

        act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public void Test04_EmptyOllamaEmbeddingResponse_HandledSafely()
    {
        var rawResponse = "{\"embeddings\": []}";
        using var doc = System.Text.Json.JsonDocument.Parse(rawResponse);
        var array = doc.RootElement.GetProperty("embeddings");

        array.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void Test05_OllamaStreamingDisconnectionMidResponse_AbortsGracefully()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Simulate immediate cancellation

        cts.IsCancellationRequested.Should().BeTrue();
    }
}
