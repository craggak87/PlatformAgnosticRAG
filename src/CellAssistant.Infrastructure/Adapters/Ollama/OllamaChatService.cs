namespace CellAssistant.Infrastructure.Adapters.Ollama;

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Application.Options;
using CellAssistant.Application.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class OllamaChatService : IChatService
{
    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaChatService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public OllamaChatService(
        HttpClient httpClient,
        IOptions<OllamaOptions> options,
        ILogger<OllamaChatService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> GenerateAnswerAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt cannot be null or whitespace.", nameof(prompt));

        // 1. Try Primary Model
        try
        {
            return await ExecuteChatCompletionAsync(_options.ChatModel, prompt, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
        {
            _logger.LogWarning(ex,
                "Primary chat model '{PrimaryModel}' failed. Automatically falling back to '{FallbackModel}'.",
                _options.ChatModel,
                _options.FallbackChatModel);
        }

        // 2. Try Fallback Model
        return await ExecuteChatCompletionAsync(_options.FallbackChatModel, prompt, cancellationToken);
    }

    private async Task<string> ExecuteChatCompletionAsync(
        string modelName,
        string prompt,
        CancellationToken cancellationToken)
    {
        var messages = new[] { new OllamaChatMessage("user", prompt) };
        var requestPayload = new OllamaChatRequest(modelName, messages, Stream: false);

        using var response = await _httpClient.PostAsJsonAsync("/api/chat", requestPayload, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Ollama chat call to model '{modelName}' failed with status {(int)response.StatusCode} ({response.ReasonPhrase}): {errorBody}");
        }

        var data = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(JsonOptions, cancellationToken);
        if (data == null)
            throw new InvalidOperationException($"Ollama returned null chat response for model '{modelName}'.");

        if (!string.IsNullOrEmpty(data.Error))
            throw new InvalidOperationException($"Ollama error from model '{modelName}': {data.Error}");

        return data.Message?.Content ?? string.Empty;
    }

    public async Task<string> StreamAnswerAsync(
        string prompt,
        Func<string, Task> onToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt cannot be null or whitespace.", nameof(prompt));
        ArgumentNullException.ThrowIfNull(onToken);

        // 1. Try Streaming with Primary Model
        try
        {
            return await ExecuteStreamChatAsync(_options.ChatModel, prompt, onToken, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
        {
            _logger.LogWarning(ex,
                "Primary chat model streaming '{PrimaryModel}' failed prior to token completion. Falling back to '{FallbackModel}'.",
                _options.ChatModel,
                _options.FallbackChatModel);
        }

        // 2. Fallback Streaming
        return await ExecuteStreamChatAsync(_options.FallbackChatModel, prompt, onToken, cancellationToken);
    }

    private async Task<string> ExecuteStreamChatAsync(
        string modelName,
        string prompt,
        Func<string, Task> onToken,
        CancellationToken cancellationToken)
    {
        var messages = new[] { new OllamaChatMessage("user", prompt) };
        var requestPayload = new OllamaChatRequest(modelName, messages, Stream: true);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(requestPayload)
        };

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Ollama stream call to model '{modelName}' failed with status {(int)response.StatusCode}: {errorBody}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var fullResponseBuilder = new StringBuilder();
        int tokenCount = 0;

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            OllamaChatStreamChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<OllamaChatStreamChunk>(line, JsonOptions);
            }
            catch (JsonException ex)
            {
                if (tokenCount == 0) throw;
                _logger.LogWarning(ex, "Failed to parse streaming token chunk: {Line}", line);
                continue;
            }

            if (!string.IsNullOrEmpty(chunk?.Error))
            {
                if (tokenCount == 0)
                    throw new InvalidOperationException($"Ollama error from model '{modelName}': {chunk.Error}");
                break;
            }

            var token = chunk?.Message?.Content;
            if (!string.IsNullOrEmpty(token))
            {
                tokenCount++;
                fullResponseBuilder.Append(token);
                await onToken(token);
            }

            if (chunk?.Done == true)
            {
                break;
            }
        }

        return fullResponseBuilder.ToString();
    }

    /// <summary>
    /// Health discovery verifying Ollama connectivity and model presence via GET /api/tags
    /// </summary>
    public async Task<(bool IsHealthy, string Description)> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync("/api/tags", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (false, $"Ollama /api/tags returned status {(int)response.StatusCode}");
            }

            var tags = await response.Content.ReadFromJsonAsync<OllamaTagsResponse>(JsonOptions, cancellationToken);
            var models = tags?.Models != null
                ? string.Join(", ", tags.Models.Select(m => m.Name))
                : "none";

            return (true, $"Ollama active. Available models: {models}");
        }
        catch (Exception ex)
        {
            return (false, $"Ollama unreachable: {ex.Message}");
        }
    }
}
