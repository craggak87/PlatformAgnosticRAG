namespace CellAssistant.Application.DTOs;

using System;
using System.Text.Json.Serialization;

/// <summary>
/// Standard error response preventing stack trace leakage.
/// </summary>
public record ApiErrorDto
{
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("correlationId")]
    public string CorrelationId { get; init; } = string.Empty;

    [JsonPropertyName("error")]
    public string Error { get; init; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public ApiErrorDto() { }

    public ApiErrorDto(string message, string correlationId)
    {
        Message = message;
        CorrelationId = correlationId;
        Error = message;
        Timestamp = DateTimeOffset.UtcNow;
    }

    public ApiErrorDto(string message, string correlationId, string? error, DateTimeOffset? timestamp = null)
    {
        Message = message;
        CorrelationId = correlationId;
        Error = error ?? message;
        Timestamp = timestamp ?? DateTimeOffset.UtcNow;
    }
}
