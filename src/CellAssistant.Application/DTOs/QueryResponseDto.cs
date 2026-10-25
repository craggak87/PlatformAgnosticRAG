namespace CellAssistant.Application.DTOs;

using System.Collections.Generic;

/// <summary>
/// Synchronous answer response with confidence rating, score, citations, and execution time.
/// </summary>
public record QueryResponseDto(
    string Answer,
    string Confidence, // "High" | "Medium" | "Low"
    IReadOnlyList<SourceReferenceDto> Sources,
    double? ConfidenceScore = null,
    IReadOnlyList<SourceReferenceDto>? RetrievedSources = null,
    long? ExecutionTimeMs = null
)
{
    public IReadOnlyList<SourceReferenceDto> RetrievedSources { get; init; } = RetrievedSources ?? Sources;
}
