namespace CellAssistant.Application.DTOs;

using System.Collections.Generic;

/// <summary>
/// Metadata event emitted for Server-Sent Events before tokens begin streaming.
/// </summary>
public record QueryMetadataDto(
    string Confidence, // "High" | "Medium" | "Low"
    IReadOnlyList<SourceReferenceDto> Sources,
    double? ConfidenceScore = null,
    IReadOnlyList<SourceReferenceDto>? RetrievedSources = null
)
{
    public IReadOnlyList<SourceReferenceDto> RetrievedSources { get; init; } = RetrievedSources ?? Sources;
}
