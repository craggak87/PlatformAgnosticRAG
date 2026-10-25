namespace CellAssistant.Infrastructure.Adapters.Ollama;

using System.Collections.Generic;
using System.Text.Json.Serialization;

/// <summary>
/// Modern Ollama batch/single embedding request: POST /api/embed
/// </summary>
public record OllamaEmbedRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("input")] object Input
);

/// <summary>
/// Modern Ollama batch/single embedding response: POST /api/embed
/// </summary>
public record OllamaEmbedResponse(
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("embeddings")] float[][]? Embeddings,
    [property: JsonPropertyName("error")] string? Error
);

/// <summary>
/// Legacy Ollama single embedding request: POST /api/embeddings
/// </summary>
public record OllamaLegacyEmbedRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("prompt")] string Prompt
);

/// <summary>
/// Legacy Ollama single embedding response: POST /api/embeddings
/// </summary>
public record OllamaLegacyEmbedResponse(
    [property: JsonPropertyName("embedding")] float[]? Embedding,
    [property: JsonPropertyName("error")] string? Error
);

/// <summary>
/// Ollama chat completion request: POST /api/chat
/// </summary>
public record OllamaChatRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<OllamaChatMessage> Messages,
    [property: JsonPropertyName("stream")] bool Stream
);

/// <summary>
/// Role and content message for Ollama chat API.
/// </summary>
public record OllamaChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content
);

/// <summary>
/// Synchronous chat completion response: POST /api/chat (stream: false)
/// </summary>
public record OllamaChatResponse(
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("created_at")] string? CreatedAt,
    [property: JsonPropertyName("message")] OllamaChatMessage? Message,
    [property: JsonPropertyName("done")] bool Done,
    [property: JsonPropertyName("total_duration")] long? TotalDuration,
    [property: JsonPropertyName("error")] string? Error
);

/// <summary>
/// Individual streaming chunk emitted as newline-delimited JSON: POST /api/chat (stream: true)
/// </summary>
public record OllamaChatStreamChunk(
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("created_at")] string? CreatedAt,
    [property: JsonPropertyName("message")] OllamaChatMessage? Message,
    [property: JsonPropertyName("done")] bool Done,
    [property: JsonPropertyName("total_duration")] long? TotalDuration,
    [property: JsonPropertyName("error")] string? Error
);

/// <summary>
/// Model discovery response: GET /api/tags
/// </summary>
public record OllamaTagsResponse(
    [property: JsonPropertyName("models")] IReadOnlyList<OllamaModelItem>? Models
);

public record OllamaModelItem(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("modified_at")] string? ModifiedAt,
    [property: JsonPropertyName("size")] long? Size
);
