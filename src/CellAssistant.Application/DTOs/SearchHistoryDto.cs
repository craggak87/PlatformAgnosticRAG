namespace CellAssistant.Application.DTOs;

/// <summary>
/// Historical search query entry for GET /api/history.
/// </summary>
public record SearchHistoryDto(
    Guid Id,
    string Question,
    string Answer,
    string Confidence,
    IReadOnlyList<SourceReferenceDto> Sources,
    DateTimeOffset AskedUtc
);
