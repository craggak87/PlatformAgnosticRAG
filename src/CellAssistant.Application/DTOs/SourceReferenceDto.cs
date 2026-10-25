namespace CellAssistant.Application.DTOs;

/// <summary>
/// Structured source reference pointing directly to a file, page, or section.
/// </summary>
public record SourceReferenceDto(
    Guid DocumentId,
    string FileName,
    string RelativePath,
    int? PageNumber = null,
    string? SectionHeading = null
);
