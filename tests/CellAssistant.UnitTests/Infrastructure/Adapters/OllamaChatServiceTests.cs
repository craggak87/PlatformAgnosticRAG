namespace CellAssistant.UnitTests.Infrastructure.Adapters;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Application.Options;
using CellAssistant.Infrastructure.Adapters.Ollama;
using CellAssistant.UnitTests.Infrastructure.Common;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

public class OllamaChatServiceTests
{
    private readonly OllamaOptions _options;
    private readonly IOptions<OllamaOptions> _optionsWrapper;

    public OllamaChatServiceTests()
    {
        _options = new OllamaOptions
        {
            BaseUrl = "http://localhost:11434",
            ChatModel = "gemma4:e4b-it-q4_K_M",
            FallbackChatModel = "qwen3.8:latest",
            RequestTimeoutSeconds = 120
        };
        _optionsWrapper = Options.Create(_options);
    }

    [Fact]
    public async Task GenerateAnswerAsync_PrimaryModelSuccess_ReturnsAnswerString()
    {
        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            req.RequestUri!.AbsolutePath.Should().Be("/api/chat");
            var reqData = await req.Content!.ReadFromJsonAsync<OllamaChatRequest>(cancellationToken: ct);
            reqData!.Model.Should().Be("gemma4:e4b-it-q4_K_M");
            reqData.Stream.Should().BeFalse();

            var responsePayload = new OllamaChatResponse(
                Model: "gemma4:e4b-it-q4_K_M",
                CreatedAt: DateTime.UtcNow.ToString("O"),
                Message: new OllamaChatMessage("assistant", "Check limit switch I:0/15."),
                Done: true,
                TotalDuration: 123456,
                Error: null);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(responsePayload))
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri(_options.BaseUrl) };
        var service = new OllamaChatService(client, _optionsWrapper, NullLogger<OllamaChatService>.Instance);

        var answer = await service.GenerateAnswerAsync("What causes gripper timeout?");
        answer.Should().Be("Check limit switch I:0/15.");
    }

    [Fact]
    public async Task GenerateAnswerAsync_PrimaryModelFails_FallsBackToQwenModel()
    {
        var attemptedModels = new List<string>();

        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            var reqData = await req.Content!.ReadFromJsonAsync<OllamaChatRequest>(cancellationToken: ct);
            attemptedModels.Add(reqData!.Model);

            if (reqData.Model == "gemma4:e4b-it-q4_K_M")
            {
                // Primary model not found (404)
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{\"error\":\"model not found\"}")
                };
            }

            var responsePayload = new OllamaChatResponse(
                Model: "qwen3.8:latest",
                CreatedAt: DateTime.UtcNow.ToString("O"),
                Message: new OllamaChatMessage("assistant", "Fallback answer from Qwen."),
                Done: true,
                TotalDuration: 654321,
                Error: null);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(responsePayload))
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri(_options.BaseUrl) };
        var service = new OllamaChatService(client, _optionsWrapper, NullLogger<OllamaChatService>.Instance);

        var answer = await service.GenerateAnswerAsync("Troubleshoot gripper");

        answer.Should().Be("Fallback answer from Qwen.");
        attemptedModels.Should().ContainInOrder("gemma4:e4b-it-q4_K_M", "qwen3.8:latest");
    }

    [Fact]
    public async Task GenerateAnswerAsync_BothModelsFail_ThrowsHttpRequestException()
    {
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("{\"error\":\"daemon crash\"}")
            });
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri(_options.BaseUrl) };
        var service = new OllamaChatService(client, _optionsWrapper, NullLogger<OllamaChatService>.Instance);

        var act = async () => await service.GenerateAnswerAsync("Test prompt");
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task GenerateAnswerAsync_NullOrWhitespacePrompt_ThrowsArgumentException(string? prompt)
    {
        var handler = new MockHttpMessageHandler((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var client = new HttpClient(handler) { BaseAddress = new Uri(_options.BaseUrl) };
        var service = new OllamaChatService(client, _optionsWrapper, NullLogger<OllamaChatService>.Instance);

        var act = async () => await service.GenerateAnswerAsync(prompt!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task StreamAnswerAsync_PrimaryModelSuccess_EmitsTokensAndReturnsAccumulatedText()
    {
        var emittedTokens = new List<string>();

        var sseLines = new[]
        {
            JsonSerializer.Serialize(new OllamaChatStreamChunk("gemma4", null, new OllamaChatMessage("assistant", "Check "), false, null, null)),
            JsonSerializer.Serialize(new OllamaChatStreamChunk("gemma4", null, new OllamaChatMessage("assistant", "valve "), false, null, null)),
            JsonSerializer.Serialize(new OllamaChatStreamChunk("gemma4", null, new OllamaChatMessage("assistant", "VFD01."), true, null, null))
        };
        var sseBody = string.Join("\n", sseLines) + "\n";

        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sseBody)
            });
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri(_options.BaseUrl) };
        var service = new OllamaChatService(client, _optionsWrapper, NullLogger<OllamaChatService>.Instance);

        var fullAnswer = await service.StreamAnswerAsync(
            prompt: "What about VFD01?",
            onToken: token =>
            {
                emittedTokens.Add(token);
                return Task.CompletedTask;
            });

        fullAnswer.Should().Be("Check valve VFD01.");
        emittedTokens.Should().ContainInOrder("Check ", "valve ", "VFD01.");
    }

    [Fact]
    public async Task StreamAnswerAsync_PrimaryModelFailsBeforeTokens_FallsBackToSecondaryStream()
    {
        var attemptedModels = new List<string>();
        var emittedTokens = new List<string>();

        var sseLines = new[]
        {
            JsonSerializer.Serialize(new OllamaChatStreamChunk("qwen3.8:latest", null, new OllamaChatMessage("assistant", "Fallback token"), true, null, null))
        };
        var fallbackBody = string.Join("\n", sseLines) + "\n";

        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            var reqData = await req.Content!.ReadFromJsonAsync<OllamaChatRequest>(cancellationToken: ct);
            attemptedModels.Add(reqData!.Model);

            if (reqData.Model == "gemma4:e4b-it-q4_K_M")
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("{\"error\":\"model loading OOM\"}")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(fallbackBody)
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri(_options.BaseUrl) };
        var service = new OllamaChatService(client, _optionsWrapper, NullLogger<OllamaChatService>.Instance);

        var result = await service.StreamAnswerAsync(
            "Test fallback stream",
            onToken: token =>
            {
                emittedTokens.Add(token);
                return Task.CompletedTask;
            });

        result.Should().Be("Fallback token");
        attemptedModels.Should().ContainInOrder("gemma4:e4b-it-q4_K_M", "qwen3.8:latest");
        emittedTokens.Should().ContainSingle().Which.Should().Be("Fallback token");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenTagsEndpointAvailable_ReturnsHealthy()
    {
        var tagsResponse = new OllamaTagsResponse(new[]
        {
            new OllamaModelItem("gemma4:e4b-it-q4_K_M", null, 1000000),
            new OllamaModelItem("embeddinggemma-2:latest", null, 500000)
        });

        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            req.RequestUri!.AbsolutePath.Should().Be("/api/tags");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(tagsResponse))
            });
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri(_options.BaseUrl) };
        var service = new OllamaChatService(client, _optionsWrapper, NullLogger<OllamaChatService>.Instance);

        var (isHealthy, desc) = await service.CheckHealthAsync();

        isHealthy.Should().BeTrue();
        desc.Should().Contain("gemma4:e4b-it-q4_K_M");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenOllamaDown_ReturnsUnhealthy()
    {
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            throw new HttpRequestException("Connection refused");
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri(_options.BaseUrl) };
        var service = new OllamaChatService(client, _optionsWrapper, NullLogger<OllamaChatService>.Instance);

        var (isHealthy, desc) = await service.CheckHealthAsync();

        isHealthy.Should().BeFalse();
        desc.Should().Contain("unreachable");
    }
}
