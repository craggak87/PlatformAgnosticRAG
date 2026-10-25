namespace CellAssistant.Application.DTOs;

using System;
using System.Text.Json.Serialization;

/// <summary>
/// Response payload for POST /api/admin/reindex.
/// </summary>
public record ReindexResponseDto
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("reindexRequested")]
    public bool ReindexRequested { get; init; } = true;

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("triggeredUtc")]
    public DateTime TriggeredUtc { get; init; } = DateTime.UtcNow;

    public ReindexResponseDto() { }

    public ReindexResponseDto(string status, bool reindexRequested, DateTimeOffset timestamp)
    {
        Status = status;
        ReindexRequested = reindexRequested;
        Timestamp = timestamp;
        TriggeredUtc = timestamp.UtcDateTime;
    }

    public ReindexResponseDto(string status, DateTime triggeredUtc)
    {
        Status = status;
        ReindexRequested = true;
        TriggeredUtc = triggeredUtc;
        Timestamp = new DateTimeOffset(triggeredUtc, TimeSpan.Zero);
    }

    public ReindexResponseDto(string status, bool reindexRequested, DateTimeOffset timestamp, DateTime? triggeredUtc)
    {
        Status = status;
        ReindexRequested = reindexRequested;
        Timestamp = timestamp;
        TriggeredUtc = triggeredUtc ?? timestamp.UtcDateTime;
    }
}
