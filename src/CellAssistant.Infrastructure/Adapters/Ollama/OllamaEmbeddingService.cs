namespace CellAssistant.Infrastructure.Adapters.Ollama;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Application.Options;
using CellAssistant.Application.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class OllamaEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaEmbeddingService> _logger;
    private const int ExpectedVectorDimension = 768;
    private const int MaxBatchSize = 32;
    private const int MaxRetries = 3;

    public OllamaEmbeddingService(
        HttpClient httpClient,
        IOptions<OllamaOptions> options,
        ILogger<OllamaEmbeddingService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text cannot be null or whitespace for embedding generation.", nameof(text));

        var batchResult = await GenerateBatchEmbeddingsAsync(new[] { text }, cancellationToken);
        if (batchResult.Count == 0)
            throw new InvalidOperationException("Failed to generate embedding: empty result returned.");

        return batchResult[0];
    }

    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchEmbeddingsAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        if (texts.Count == 0)
            return Array.Empty<ReadOnlyMemory<float>>();

        var results = new List<ReadOnlyMemory<float>>(texts.Count);

        for (int i = 0; i < texts.Count; i += MaxBatchSize)
        {
            var currentBatchCount = Math.Min(MaxBatchSize, texts.Count - i);
            var batch = new string[currentBatchCount];
            for (int b = 0; b < currentBatchCount; b++)
            {
                batch[b] = texts[i + b];
            }

            var batchVectors = await ExecuteBatchWithRetryAsync(batch, cancellationToken);
            results.AddRange(batchVectors);
        }

        return results;
    }

    private async Task<IReadOnlyList<ReadOnlyMemory<float>>> ExecuteBatchWithRetryAsync(
        IReadOnlyList<string> batch,
        CancellationToken cancellationToken)
    {
        int maxAttempts = MaxRetries;
        int delayMs = 500;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await ExecuteEmbedEndpointAsync(batch, cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex, "Ollama embedding call failed on attempt {Attempt}/{MaxAttempts}. Retrying in {DelayMs}ms...", attempt, maxAttempts, delayMs);
                await Task.Delay(delayMs, cancellationToken);
                delayMs *= 2;
            }
        }

        return await ExecuteEmbedEndpointAsync(batch, cancellationToken);
    }

    private async Task<IReadOnlyList<ReadOnlyMemory<float>>> ExecuteEmbedEndpointAsync(
        IReadOnlyList<string> batch,
        CancellationToken cancellationToken)
    {
        var requestPayload = new OllamaEmbedRequest(_options.EmbeddingModel, batch);
        using var response = await _httpClient.PostAsJsonAsync("/api/embed", requestPayload, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Ollama /api/embed returned 404. Falling back to legacy /api/embeddings endpoint.");
            return await ExecuteLegacyEmbeddingsAsync(batch, cancellationToken);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Ollama embedding call failed with status {(int)response.StatusCode} ({response.ReasonPhrase}): {errorBody}");
        }

        var data = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(cancellationToken: cancellationToken);
        if (data == null)
            throw new InvalidOperationException("Ollama returned null embedding response.");

        if (!string.IsNullOrEmpty(data.Error))
            throw new InvalidOperationException($"Ollama embedding error: {data.Error}");

        if (data.Embeddings == null || data.Embeddings.Length != batch.Count)
        {
            throw new InvalidOperationException(
                $"Ollama returned {data.Embeddings?.Length ?? 0} embeddings, but {batch.Count} were requested.");
        }

        var list = new List<ReadOnlyMemory<float>>(data.Embeddings.Length);
        foreach (var vector in data.Embeddings)
        {
            if (vector.Length != ExpectedVectorDimension)
            {
                throw new InvalidOperationException(
                    $"Expected {ExpectedVectorDimension} dimensions from '{_options.EmbeddingModel}', but received {vector.Length}.");
            }
            list.Add(new ReadOnlyMemory<float>(vector));
        }

        return list;
    }

    private async Task<IReadOnlyList<ReadOnlyMemory<float>>> ExecuteLegacyEmbeddingsAsync(
        IReadOnlyList<string> batch,
        CancellationToken cancellationToken)
    {
        var list = new List<ReadOnlyMemory<float>>(batch.Count);

        foreach (var text in batch)
        {
            var legacyPayload = new OllamaLegacyEmbedRequest(_options.EmbeddingModel, text);
            using var legacyResponse = await _httpClient.PostAsJsonAsync("/api/embeddings", legacyPayload, cancellationToken);

            if (!legacyResponse.IsSuccessStatusCode)
            {
                var errorBody = await legacyResponse.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException(
                    $"Legacy Ollama embedding call failed with status {(int)legacyResponse.StatusCode}: {errorBody}");
            }

            var data = await legacyResponse.Content.ReadFromJsonAsync<OllamaLegacyEmbedResponse>(cancellationToken: cancellationToken);
            if (data == null || data.Embedding == null)
                throw new InvalidOperationException("Legacy Ollama /api/embeddings returned null embedding vector.");

            if (!string.IsNullOrEmpty(data.Error))
                throw new InvalidOperationException($"Legacy Ollama embedding error: {data.Error}");

            if (data.Embedding.Length != ExpectedVectorDimension)
            {
                throw new InvalidOperationException(
                    $"Expected {ExpectedVectorDimension} dimensions from '{_options.EmbeddingModel}', but received {data.Embedding.Length}.");
            }

            list.Add(new ReadOnlyMemory<float>(data.Embedding));
        }

        return list;
    }
}
