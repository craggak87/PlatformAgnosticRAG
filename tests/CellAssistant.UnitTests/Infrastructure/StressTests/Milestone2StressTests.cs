namespace CellAssistant.UnitTests.Infrastructure.StressTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Application.Options;
using CellAssistant.Domain.Entities;
using CellAssistant.Infrastructure.Adapters.Ollama;
using CellAssistant.Infrastructure.Persistence.Qdrant;
using CellAssistant.UnitTests.Infrastructure.Common;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using global::Qdrant.Client;
using global::Qdrant.Client.Grpc;

public class Milestone2StressTests
{
    private readonly QdrantOptions _qdrantOptions;
    private readonly IOptions<QdrantOptions> _qdrantOptionsWrapper;
    private readonly OllamaOptions _ollamaOptions;
    private readonly IOptions<OllamaOptions> _ollamaOptionsWrapper;

    public Milestone2StressTests()
    {
        _qdrantOptions = new QdrantOptions
        {
            Host = "localhost",
            GrpcPort = 6334,
            CollectionName = "cell-knowledge",
            VectorSize = 768
        };
        _qdrantOptionsWrapper = Options.Create(_qdrantOptions);

        _ollamaOptions = new OllamaOptions
        {
            BaseUrl = "http://localhost:11434",
            ChatModel = "gemma4:e4b-it-q4_K_M",
            FallbackChatModel = "qwen3.8:latest",
            EmbeddingModel = "embeddinggemma-2:latest",
            RequestTimeoutSeconds = 120
        };
        _ollamaOptionsWrapper = Options.Create(_ollamaOptions);
    }

    // =========================================================================
    // 1. QdrantVectorStore Empirical Verification
    // =========================================================================

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(512)]
    [InlineData(767)]
    [InlineData(769)]
    [InlineData(1536)]
    public async Task QdrantVectorStore_UpsertVectorsAsync_RejectsInvalidVectorDimensions(int dimension)
    {
        var client = Substitute.For<IQdrantClient>();
        var store = new QdrantVectorStore(client, _qdrantOptionsWrapper, NullLogger<QdrantVectorStore>.Instance);

        var pt = new VectorPoint
        {
            ChunkId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Vector = new float[dimension]
        };

        var act = async () => await store.UpsertVectorsAsync(new[] { pt });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"*does not match required size {_qdrantOptions.VectorSize}*");

        await client.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default!, default, default, default, default);
    }

    [Fact]
    public async Task QdrantVectorStore_UpsertVectorsAsync_RejectsNullVector()
    {
        var client = Substitute.For<IQdrantClient>();
        var store = new QdrantVectorStore(client, _qdrantOptionsWrapper, NullLogger<QdrantVectorStore>.Instance);

        var pt = new VectorPoint
        {
            ChunkId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Vector = null!
        };

        var act = async () => await store.UpsertVectorsAsync(new[] { pt });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"*does not match required size {_qdrantOptions.VectorSize}*");

        await client.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default!, default, default, default, default);
    }

    [Theory]
    [InlineData(0, false)]    // NaN at start
    [InlineData(384, false)]  // NaN in middle
    [InlineData(767, false)]  // NaN at end
    [InlineData(0, true)]     // Infinity at start
    [InlineData(384, true)]   // Infinity in middle
    [InlineData(767, true)]   // Infinity at end
    public async Task QdrantVectorStore_UpsertVectorsAsync_RejectsNaNAndPositiveInfinity(int index, bool isInfinity)
    {
        var client = Substitute.For<IQdrantClient>();
        var store = new QdrantVectorStore(client, _qdrantOptionsWrapper, NullLogger<QdrantVectorStore>.Instance);

        var vector = new float[768];
        vector[index] = isInfinity ? float.PositiveInfinity : float.NaN;

        var pt = new VectorPoint
        {
            ChunkId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Vector = vector
        };

        var act = async () => await store.UpsertVectorsAsync(new[] { pt });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"*contains invalid float at index {index}*");

        await client.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default!, default, default, default, default);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(767)]
    public async Task QdrantVectorStore_UpsertVectorsAsync_RejectsNegativeInfinity(int index)
    {
        var client = Substitute.For<IQdrantClient>();
        var store = new QdrantVectorStore(client, _qdrantOptionsWrapper, NullLogger<QdrantVectorStore>.Instance);

        var vector = new float[768];
        vector[index] = float.NegativeInfinity;

        var pt = new VectorPoint
        {
            ChunkId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Vector = vector
        };

        var act = async () => await store.UpsertVectorsAsync(new[] { pt });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"*contains invalid float at index {index}*");

        await client.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default!, default, default, default, default);
    }

    [Fact]
    public async Task QdrantVectorStore_UpsertVectorsAsync_EmptyOrNullList_ReturnsImmediately()
    {
        var client = Substitute.For<IQdrantClient>();
        var store = new QdrantVectorStore(client, _qdrantOptionsWrapper, NullLogger<QdrantVectorStore>.Instance);

        await store.UpsertVectorsAsync(Array.Empty<VectorPoint>());
        await store.UpsertVectorsAsync(null!);

        await client.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default!, default, default, default, default);
    }

    [Fact]
    public async Task QdrantVectorStore_UpsertVectorsAsync_250Points_PartitionedInBatchesOf100()
    {
        var client = Substitute.For<IQdrantClient>();
        var capturedBatches = new List<IReadOnlyList<PointStruct>>();

        client.UpsertAsync(
            _qdrantOptions.CollectionName,
            Arg.Do<IReadOnlyList<PointStruct>>(batch => capturedBatches.Add(batch.ToList())),
            wait: true,
            ordering: null,
            shardKeySelector: null,
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new UpdateResult { Status = UpdateStatus.Completed }));

        var store = new QdrantVectorStore(client, _qdrantOptionsWrapper, NullLogger<QdrantVectorStore>.Instance);

        var points = new List<VectorPoint>();
        for (int i = 0; i < 250; i++)
        {
            points.Add(new VectorPoint
            {
                ChunkId = Guid.NewGuid(),
                DocumentId = Guid.NewGuid(),
                Vector = new float[768],
                Content = $"Chunk {i}",
                FileName = $"File_{i}.md",
                RelativePath = $"Folder/File_{i}.md",
                Folder = "Folder"
            });
        }

        await store.UpsertVectorsAsync(points);

        capturedBatches.Should().HaveCount(3);
        capturedBatches[0].Should().HaveCount(100);
        capturedBatches[1].Should().HaveCount(100);
        capturedBatches[2].Should().HaveCount(50);
    }

    // =========================================================================
    // 2. OllamaEmbeddingService Empirical Verification
    // =========================================================================

    [Fact]
    public async Task OllamaEmbeddingService_BatchPartitioning_105Items_ChunksIntoBatchesOf32()
    {
        var capturedBatchSizes = new List<int>();

        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            req.RequestUri!.AbsolutePath.Should().Be("/api/embed");
            var reqData = await req.Content!.ReadFromJsonAsync<OllamaEmbedRequest>(cancellationToken: ct);
            var inputs = (JsonElement)reqData!.Input;
            var count = inputs.GetArrayLength();
            capturedBatchSizes.Add(count);

            var embeddings = new float[count][];
            for (int i = 0; i < count; i++)
            {
                var vec = new float[768];
                vec[0] = i; // identify item
                embeddings[i] = vec;
            }

            var resp = new OllamaEmbedResponse("embeddinggemma-2:latest", embeddings, null);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(resp))
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri(_ollamaOptions.BaseUrl) };
        var service = new OllamaEmbeddingService(client, _ollamaOptionsWrapper, NullLogger<OllamaEmbeddingService>.Instance);

        var texts = Enumerable.Range(0, 105).Select(i => $"Document item {i}").ToList();

        var results = await service.GenerateBatchEmbeddingsAsync(texts);

        results.Should().HaveCount(105);
        capturedBatchSizes.Should().Equal(32, 32, 32, 9);

        // Verify ordering is maintained
        for (int i = 0; i < 105; i++)
        {
            results[i].Length.Should().Be(768);
        }
    }

    [Fact]
    public async Task OllamaEmbeddingService_Http404FallbackToLegacyEmbeddings_Succeeds()
    {
        var legacyCalls = 0;
        var embedCalls = 0;

        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/embed")
            {
                Interlocked.Increment(ref embedCalls);
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{\"error\":\"404 not found\"}")
                };
            }

            if (req.RequestUri!.AbsolutePath == "/api/embeddings")
            {
                Interlocked.Increment(ref legacyCalls);
                var reqData = await req.Content!.ReadFromJsonAsync<OllamaLegacyEmbedRequest>(cancellationToken: ct);
                var vec = new float[768];
                vec[0] = 1.23f;

                var legacyResp = new OllamaLegacyEmbedResponse(vec, null);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(legacyResp))
                };
            }

            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri(_ollamaOptions.BaseUrl) };
        var service = new OllamaEmbeddingService(client, _ollamaOptionsWrapper, NullLogger<OllamaEmbeddingService>.Instance);

        var batch = new[] { "Text A", "Text B", "Text C" };
        var results = await service.GenerateBatchEmbeddingsAsync(batch);

        results.Should().HaveCount(3);
        embedCalls.Should().Be(1);
        legacyCalls.Should().Be(3); // 1 per item in batch
        results[0].Span[0].Should().Be(1.23f);
        results[1].Span[0].Should().Be(1.23f);
        results[2].Span[0].Should().Be(1.23f);
    }

    [Fact]
    public async Task OllamaEmbeddingService_Http404Fallback_WhenLegacyFails_ThrowsHttpRequestException()
    {
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/embed")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (req.RequestUri!.AbsolutePath == "/api/embeddings")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("{\"error\":\"Internal Server Error\"}")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri(_ollamaOptions.BaseUrl) };
        var service = new OllamaEmbeddingService(client, _ollamaOptionsWrapper, NullLogger<OllamaEmbeddingService>.Instance);

        var act = async () => await service.GenerateBatchEmbeddingsAsync(new[] { "Fallback failure" });
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task OllamaEmbeddingService_ExponentialBackoffRetry_RetriesUpTo3Times()
    {
        int attempts = 0;
        var start = DateTime.UtcNow;

        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new HttpRequestException($"Transient socket reset attempt {attempts}");
            }

            var resp = new OllamaEmbedResponse("embeddinggemma-2:latest", new[] { new float[768] }, null);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(resp))
            });
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri(_ollamaOptions.BaseUrl) };
        var service = new OllamaEmbeddingService(client, _ollamaOptionsWrapper, NullLogger<OllamaEmbeddingService>.Instance);

        var result = await service.GenerateBatchEmbeddingsAsync(new[] { "Retry success test" });

        var elapsed = DateTime.UtcNow - start;
        attempts.Should().Be(3);
        // Backoff: 500ms after attempt 1, 1000ms after attempt 2 = ~1500ms
        elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(1300));
        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task OllamaEmbeddingService_ExponentialBackoffRetry_FailsAfter3Attempts()
    {
        int attempts = 0;

        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            attempts++;
            throw new HttpRequestException($"Persistent connection failure {attempts}");
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri(_ollamaOptions.BaseUrl) };
        var service = new OllamaEmbeddingService(client, _ollamaOptionsWrapper, NullLogger<OllamaEmbeddingService>.Instance);

        var act = async () => await service.GenerateBatchEmbeddingsAsync(new[] { "Fail test" });
        await act.Should().ThrowAsync<HttpRequestException>();

        attempts.Should().Be(3);
    }

    // =========================================================================
    // 3. OllamaChatService Empirical Verification
    // =========================================================================

    [Fact]
    public async Task OllamaChatService_StreamAnswerAsync_InitialMalformedJson_TriggersFallbackModel()
    {
        var modelsCalled = new List<string>();
        var tokensEmitted = new List<string>();

        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            var reqData = await req.Content!.ReadFromJsonAsync<OllamaChatRequest>(cancellationToken: ct);
            modelsCalled.Add(reqData!.Model);

            if (reqData.Model == "gemma4:e4b-it-q4_K_M")
            {
                // Returns 200 OK but body is malformed HTML/text (e.g. proxy error)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html>502 Bad Gateway</html>\n")
                };
            }

            // Fallback model returns valid JSON streaming chunks
            var fallbackChunk = JsonSerializer.Serialize(new OllamaChatStreamChunk(
                Model: "qwen3.8:latest",
                CreatedAt: null,
                Message: new OllamaChatMessage("assistant", "Recovered via fallback"),
                Done: true,
                TotalDuration: null,
                Error: null));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(fallbackChunk + "\n")
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri(_ollamaOptions.BaseUrl) };
        var service = new OllamaChatService(client, _ollamaOptionsWrapper, NullLogger<OllamaChatService>.Instance);

        var finalAnswer = await service.StreamAnswerAsync(
            "Test initial malformed JSON",
            token =>
            {
                tokensEmitted.Add(token);
                return Task.CompletedTask;
            });

        finalAnswer.Should().Be("Recovered via fallback");
        modelsCalled.Should().Equal("gemma4:e4b-it-q4_K_M", "qwen3.8:latest");
        tokensEmitted.Should().ContainSingle().Which.Should().Be("Recovered via fallback");
    }

    [Fact]
    public async Task OllamaChatService_StreamAnswerAsync_MidStreamMalformedChunk_SkipsChunkAndContinues()
    {
        var modelsCalled = new List<string>();
        var tokensEmitted = new List<string>();

        var validChunk1 = JsonSerializer.Serialize(new OllamaChatStreamChunk(
            Model: "gemma4:e4b-it-q4_K_M",
            CreatedAt: null,
            Message: new OllamaChatMessage("assistant", "Step 1: Inspect "),
            Done: false,
            TotalDuration: null,
            Error: null));

        // Corrupted JSON chunk in the middle of stream
        var corruptChunk = "{corrupted: malformed JSON text without proper syntax";

        var validChunk2 = JsonSerializer.Serialize(new OllamaChatStreamChunk(
            Model: "gemma4:e4b-it-q4_K_M",
            CreatedAt: null,
            Message: new OllamaChatMessage("assistant", "cable I:0/15."),
            Done: true,
            TotalDuration: null,
            Error: null));

        var streamBody = string.Join("\n", validChunk1, "", "   ", corruptChunk, validChunk2) + "\n";

        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            var reqData = await req.Content!.ReadFromJsonAsync<OllamaChatRequest>(cancellationToken: ct);
            modelsCalled.Add(reqData!.Model);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(streamBody)
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri(_ollamaOptions.BaseUrl) };
        var service = new OllamaChatService(client, _ollamaOptionsWrapper, NullLogger<OllamaChatService>.Instance);

        var fullAnswer = await service.StreamAnswerAsync(
            "Test mid-stream malformed line",
            token =>
            {
                tokensEmitted.Add(token);
                return Task.CompletedTask;
            });

        fullAnswer.Should().Be("Step 1: Inspect cable I:0/15.");
        tokensEmitted.Should().Equal("Step 1: Inspect ", "cable I:0/15.");
        // Must NOT have called fallback model since tokens were already in flight
        modelsCalled.Should().ContainSingle().Which.Should().Be("gemma4:e4b-it-q4_K_M");
    }

    [Fact]
    public async Task OllamaChatService_StreamAnswerAsync_MidStreamErrorPayload_TerminatesWithoutFallback()
    {
        var modelsCalled = new List<string>();
        var tokensEmitted = new List<string>();

        var chunk1 = JsonSerializer.Serialize(new OllamaChatStreamChunk(
            Model: "gemma4:e4b-it-q4_K_M",
            CreatedAt: null,
            Message: new OllamaChatMessage("assistant", "Partial start. "),
            Done: false,
            TotalDuration: null,
            Error: null));

        var errorChunk = JsonSerializer.Serialize(new OllamaChatStreamChunk(
            Model: "gemma4:e4b-it-q4_K_M",
            CreatedAt: null,
            Message: null,
            Done: false,
            TotalDuration: null,
            Error: "GPU out of memory during generation"));

        var streamBody = string.Join("\n", chunk1, errorChunk) + "\n";

        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            var reqData = await req.Content!.ReadFromJsonAsync<OllamaChatRequest>(cancellationToken: ct);
            modelsCalled.Add(reqData!.Model);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(streamBody)
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri(_ollamaOptions.BaseUrl) };
        var service = new OllamaChatService(client, _ollamaOptionsWrapper, NullLogger<OllamaChatService>.Instance);

        var answer = await service.StreamAnswerAsync(
            "Prompt",
            token =>
            {
                tokensEmitted.Add(token);
                return Task.CompletedTask;
            });

        answer.Should().Be("Partial start. ");
        tokensEmitted.Should().ContainSingle().Which.Should().Be("Partial start. ");
        // Fallback should NOT be triggered
        modelsCalled.Should().ContainSingle().Which.Should().Be("gemma4:e4b-it-q4_K_M");
    }

    [Fact]
    public async Task OllamaChatService_StreamAnswerAsync_PreCancelledToken_ThrowsOperationCanceledExceptionWithoutFallback()
    {
        var modelsCalled = new List<string>();
        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            var reqData = await req.Content!.ReadFromJsonAsync<OllamaChatRequest>(cancellationToken: ct);
            modelsCalled.Add(reqData!.Model);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri(_ollamaOptions.BaseUrl) };
        var service = new OllamaChatService(client, _ollamaOptionsWrapper, NullLogger<OllamaChatService>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancel

        var act = async () => await service.StreamAnswerAsync(
            "Test pre-cancellation",
            _ => Task.CompletedTask,
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        modelsCalled.Should().BeEmpty();
    }

    [Fact]
    public async Task OllamaChatService_StreamAnswerAsync_CancelledDuringStreaming_AbortsImmediatelyWithoutFallback()
    {
        var modelsCalled = new List<string>();
        using var cts = new CancellationTokenSource();

        var chunk1 = JsonSerializer.Serialize(new OllamaChatStreamChunk(
            Model: "gemma4:e4b-it-q4_K_M",
            CreatedAt: null,
            Message: new OllamaChatMessage("assistant", "First token"),
            Done: false,
            TotalDuration: null,
            Error: null));

        var chunk2 = JsonSerializer.Serialize(new OllamaChatStreamChunk(
            Model: "gemma4:e4b-it-q4_K_M",
            CreatedAt: null,
            Message: new OllamaChatMessage("assistant", "Second token"),
            Done: false,
            TotalDuration: null,
            Error: null));

        var streamBody = string.Join("\n", chunk1, chunk2) + "\n";

        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            var reqData = await req.Content!.ReadFromJsonAsync<OllamaChatRequest>(cancellationToken: ct);
            modelsCalled.Add(reqData!.Model);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(streamBody)
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri(_ollamaOptions.BaseUrl) };
        var service = new OllamaChatService(client, _ollamaOptionsWrapper, NullLogger<OllamaChatService>.Instance);

        var answer = await service.StreamAnswerAsync(
            "Prompt",
            token =>
            {
                // Cancel token immediately upon receiving the first token
                cts.Cancel();
                return Task.CompletedTask;
            },
            cts.Token);

        // Reader terminates because !cancellationToken.IsCancellationRequested triggers
        answer.Should().Be("First token");
        modelsCalled.Should().ContainSingle().Which.Should().Be("gemma4:e4b-it-q4_K_M");
    }

    [Fact]
    public async Task OllamaChatService_GenerateAnswerAsync_PreCancelledToken_ThrowsOperationCanceledExceptionWithoutFallback()
    {
        var modelsCalled = new List<string>();
        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            var reqData = await req.Content!.ReadFromJsonAsync<OllamaChatRequest>(cancellationToken: ct);
            modelsCalled.Add(reqData!.Model);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri(_ollamaOptions.BaseUrl) };
        var service = new OllamaChatService(client, _ollamaOptionsWrapper, NullLogger<OllamaChatService>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await service.GenerateAnswerAsync("Prompt", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        modelsCalled.Should().BeEmpty();
    }

    [Fact]
    public async Task QdrantVectorStore_UpsertVectorsAsync_SubnormalAndNegativeFloats_Allowed()
    {
        var client = Substitute.For<IQdrantClient>();
        var store = new QdrantVectorStore(client, _qdrantOptionsWrapper, NullLogger<QdrantVectorStore>.Instance);

        var vector = new float[768];
        vector[0] = float.Epsilon; // Subnormal float
        vector[1] = -1.0f;
        vector[2] = 0.0f;
        vector[3] = -0.0f;

        var pt = new VectorPoint
        {
            ChunkId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Vector = vector
        };

        await store.UpsertVectorsAsync(new[] { pt });

        await client.Received(1).UpsertAsync(
            _qdrantOptions.CollectionName,
            Arg.Is<IReadOnlyList<PointStruct>>(p => p.Count == 1),
            wait: true,
            ordering: null,
            shardKeySelector: null,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QdrantVectorStore_UpsertVectorsAsync_NormalizesBackslashesToForwardSlashesInPath()
    {
        var client = Substitute.For<IQdrantClient>();
        IReadOnlyList<PointStruct>? capturedPoints = null;

        client.UpsertAsync(
            _qdrantOptions.CollectionName,
            Arg.Do<IReadOnlyList<PointStruct>>(pts => capturedPoints = pts),
            wait: true,
            ordering: null,
            shardKeySelector: null,
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new UpdateResult { Status = UpdateStatus.Completed }));

        var store = new QdrantVectorStore(client, _qdrantOptionsWrapper, NullLogger<QdrantVectorStore>.Instance);

        var pt = new VectorPoint
        {
            ChunkId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Vector = new float[768],
            RelativePath = @"Faults\SubFolder\Document.md"
        };

        await store.UpsertVectorsAsync(new[] { pt });

        capturedPoints.Should().NotBeNull();
        capturedPoints![0].Payload["relativePath"].StringValue.Should().Be("Faults/SubFolder/Document.md");
    }

    [Fact]
    public async Task OllamaEmbeddingService_BatchCountMismatch_ThrowsInvalidOperationException()
    {
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            // Return only 1 embedding for a request of 2 texts
            var resp = new OllamaEmbedResponse("embeddinggemma-2:latest", new[] { new float[768] }, null);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(resp))
            });
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri(_ollamaOptions.BaseUrl) };
        var service = new OllamaEmbeddingService(client, _ollamaOptionsWrapper, NullLogger<OllamaEmbeddingService>.Instance);

        var act = async () => await service.GenerateBatchEmbeddingsAsync(new[] { "Doc 1", "Doc 2" });
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Ollama returned 1 embeddings, but 2 were requested*");
    }

    [Fact]
    public async Task OllamaEmbeddingService_LegacyEmbeddings_DimensionMismatch_ThrowsInvalidOperationException()
    {
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/embed")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (req.RequestUri!.AbsolutePath == "/api/embeddings")
            {
                var legacyResp = new OllamaLegacyEmbedResponse(new float[512], null); // 512 != 768
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(legacyResp))
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri(_ollamaOptions.BaseUrl) };
        var service = new OllamaEmbeddingService(client, _ollamaOptionsWrapper, NullLogger<OllamaEmbeddingService>.Instance);

        var act = async () => await service.GenerateBatchEmbeddingsAsync(new[] { "Doc" });
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Expected 768 dimensions*");
    }

    [Fact]
    public async Task OllamaChatService_StreamAnswerAsync_NullOnTokenCallback_ThrowsArgumentNullException()
    {
        var handler = new MockHttpMessageHandler((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var client = new HttpClient(handler) { BaseAddress = new Uri(_ollamaOptions.BaseUrl) };
        var service = new OllamaChatService(client, _ollamaOptionsWrapper, NullLogger<OllamaChatService>.Instance);

        var act = async () => await service.StreamAnswerAsync("Prompt", null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task OllamaChatService_StreamAnswerAsync_BothModelsFail_ThrowsHttpRequestException()
    {
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("{\"error\":\"internal fatal error\"}")
            });
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri(_ollamaOptions.BaseUrl) };
        var service = new OllamaChatService(client, _ollamaOptionsWrapper, NullLogger<OllamaChatService>.Instance);

        var act = async () => await service.StreamAnswerAsync("Prompt", _ => Task.CompletedTask);
        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
