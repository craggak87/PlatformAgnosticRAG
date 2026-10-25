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

public class OllamaEmbeddingServiceTests
{
    private readonly OllamaOptions _options;
    private readonly IOptions<OllamaOptions> _optionsWrapper;

    public OllamaEmbeddingServiceTests()
    {
        _options = new OllamaOptions
        {
            BaseUrl = "http://localhost:11434",
            EmbeddingModel = "embeddinggemma-2:latest",
            RequestTimeoutSeconds = 120
        };
        _optionsWrapper = Options.Create(_options);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_SingleText_Returns768DimensionVector()
    {
        var expectedVector = new float[768];
        expectedVector[0] = 0.5f;
        expectedVector[767] = 0.9f;

        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            req.RequestUri!.AbsolutePath.Should().Be("/api/embed");
            var json = await req.Content!.ReadAsStringAsync(ct);
            json.Should().Contain("embeddinggemma-2:latest");

            var responsePayload = new OllamaEmbedResponse("embeddinggemma-2:latest", new[] { expectedVector }, null);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(responsePayload))
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri(_options.BaseUrl) };
        var service = new OllamaEmbeddingService(client, _optionsWrapper, NullLogger<OllamaEmbeddingService>.Instance);

        var vector = await service.GenerateEmbeddingAsync("What causes gripper timeout?");

        vector.Length.Should().Be(768);
        vector.Span[0].Should().Be(0.5f);
        vector.Span[767].Should().Be(0.9f);
    }

    [Fact]
    public async Task GenerateBatchEmbeddingsAsync_MultipleTexts_ReturnsExpectedVectors()
    {
        var vec1 = new float[768];
        var vec2 = new float[768];

        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            var responsePayload = new OllamaEmbedResponse("embeddinggemma-2:latest", new[] { vec1, vec2 }, null);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(responsePayload))
            });
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri(_options.BaseUrl) };
        var service = new OllamaEmbeddingService(client, _optionsWrapper, NullLogger<OllamaEmbeddingService>.Instance);

        var result = await service.GenerateBatchEmbeddingsAsync(new[] { "Text 1", "Text 2" });

        result.Should().HaveCount(2);
        result[0].Length.Should().Be(768);
        result[1].Length.Should().Be(768);
    }

    [Fact]
    public async Task GenerateBatchEmbeddingsAsync_EmptyList_ReturnsEmptyWithoutHttpCall()
    {
        var httpCalled = false;
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            httpCalled = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri(_options.BaseUrl) };
        var service = new OllamaEmbeddingService(client, _optionsWrapper, NullLogger<OllamaEmbeddingService>.Instance);

        var result = await service.GenerateBatchEmbeddingsAsync(Array.Empty<string>());

        result.Should().BeEmpty();
        httpCalled.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task GenerateEmbeddingAsync_NullOrWhitespace_ThrowsArgumentException(string? input)
    {
        var handler = new MockHttpMessageHandler((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var client = new HttpClient(handler) { BaseAddress = new Uri(_options.BaseUrl) };
        var service = new OllamaEmbeddingService(client, _optionsWrapper, NullLogger<OllamaEmbeddingService>.Instance);

        var act = async () => await service.GenerateEmbeddingAsync(input!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GenerateBatchEmbeddingsAsync_LargeBatch_ChunksIntoSubBatchesOf32()
    {
        int requestCount = 0;
        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            Interlocked.Increment(ref requestCount);
            var reqData = await req.Content!.ReadFromJsonAsync<OllamaEmbedRequest>(cancellationToken: ct);
            var inputs = (JsonElement)reqData!.Input;
            var count = inputs.GetArrayLength();

            var embeddings = new float[count][];
            for (int i = 0; i < count; i++)
            {
                embeddings[i] = new float[768];
            }

            var resp = new OllamaEmbedResponse("embeddinggemma-2:latest", embeddings, null);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(resp))
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri(_options.BaseUrl) };
        var service = new OllamaEmbeddingService(client, _optionsWrapper, NullLogger<OllamaEmbeddingService>.Instance);

        // 70 items: should be broken into 3 batches (32 + 32 + 6)
        var texts = new List<string>();
        for (int i = 0; i < 70; i++) texts.Add($"Chunk {i}");

        var results = await service.GenerateBatchEmbeddingsAsync(texts);

        results.Should().HaveCount(70);
        requestCount.Should().Be(3);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WhenApiEmbed404_FallsBackToLegacyEmbeddings()
    {
        var legacyCalled = false;
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/embed")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (req.RequestUri!.AbsolutePath == "/api/embeddings")
            {
                legacyCalled = true;
                var legacyResp = new OllamaLegacyEmbedResponse(new float[768], null);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(legacyResp))
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri(_options.BaseUrl) };
        var service = new OllamaEmbeddingService(client, _optionsWrapper, NullLogger<OllamaEmbeddingService>.Instance);

        var vector = await service.GenerateEmbeddingAsync("Legacy test text");

        vector.Length.Should().Be(768);
        legacyCalled.Should().BeTrue();
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_TransientError_RetriesAndSucceeds()
    {
        int attempts = 0;
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new HttpRequestException("Simulated transient socket reset");
            }

            var resp = new OllamaEmbedResponse("embeddinggemma-2:latest", new[] { new float[768] }, null);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(resp))
            });
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri(_options.BaseUrl) };
        var service = new OllamaEmbeddingService(client, _optionsWrapper, NullLogger<OllamaEmbeddingService>.Instance);

        var vector = await service.GenerateEmbeddingAsync("Retry test");

        vector.Length.Should().Be(768);
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_DimensionMismatch_ThrowsInvalidOperationException()
    {
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            var wrongDimensionVector = new float[512]; // Not 768
            var resp = new OllamaEmbedResponse("embeddinggemma-2:latest", new[] { wrongDimensionVector }, null);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(resp))
            });
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri(_options.BaseUrl) };
        var service = new OllamaEmbeddingService(client, _optionsWrapper, NullLogger<OllamaEmbeddingService>.Instance);

        var act = async () => await service.GenerateEmbeddingAsync("Dimension test");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Expected 768 dimensions*");
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_OllamaErrorPayload_ThrowsInvalidOperationException()
    {
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            var errorResp = new OllamaEmbedResponse(null, null, "model 'embeddinggemma-2:latest' not found, try pulling it first");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(errorResp))
            });
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri(_options.BaseUrl) };
        var service = new OllamaEmbeddingService(client, _optionsWrapper, NullLogger<OllamaEmbeddingService>.Instance);

        var act = async () => await service.GenerateEmbeddingAsync("Error test");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Ollama embedding error*");
    }
}
