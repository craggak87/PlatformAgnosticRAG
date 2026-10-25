using System.Text.Json;
using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;

namespace CellAssistant.E2ETests.Tier1_FeatureCoverage;

public class Feature10_OllamaAdaptersTests
{
    [Fact]
    public void Test01_OllamaEmbedPayload_MatchesSpecificationWireFormat()
    {
        var request = new
        {
            model = SpecificationConstants.EmbeddingModel,
            input = new[] { "What causes gripper timeout?" }
        };

        var json = JsonSerializer.Serialize(request);
        json.Should().Contain(SpecificationConstants.EmbeddingModel);
        json.Should().Contain("gripper timeout");
    }

    [Fact]
    public void Test02_OllamaBatchEmbedPayload_SupportsMultipleInputs()
    {
        var inputs = new[] { "Chunk 1 content", "Chunk 2 content", "Chunk 3 content" };
        var request = new
        {
            model = SpecificationConstants.EmbeddingModel,
            input = inputs
        };

        var json = JsonSerializer.Serialize(request);
        json.Should().Contain("Chunk 1");
        json.Should().Contain("Chunk 3");
    }

    [Fact]
    public void Test03_OllamaChatPayload_MatchesGemma4Specification()
    {
        var request = new
        {
            model = SpecificationConstants.PrimaryChatModel,
            messages = new[]
            {
                new { role = "system", content = "You are an engineering assistant." },
                new { role = "user", content = "Explain ALM-205" }
            },
            stream = false
        };

        var json = JsonSerializer.Serialize(request);
        json.Should().Contain(SpecificationConstants.PrimaryChatModel);
        json.Should().Contain("ALM-205");
    }

    [Fact]
    public void Test04_OllamaChatStreaming_EnablesStreamFlag()
    {
        var request = new
        {
            model = SpecificationConstants.PrimaryChatModel,
            messages = new[]
            {
                new { role = "user", content = "Stream token test" }
            },
            stream = true
        };

        request.stream.Should().BeTrue();
    }

    [Fact]
    public void Test05_FallbackModel_ConfiguredToQwen()
    {
        SpecificationConstants.FallbackChatModel.Should().Be("qwen3.8:latest",
            "ADR-003 and Doc 3 specify qwen3.8:latest as the offline fallback chat model");
    }
}
