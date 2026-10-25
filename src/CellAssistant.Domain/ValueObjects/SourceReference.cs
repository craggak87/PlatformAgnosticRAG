namespace CellAssistant.Domain.ValueObjects;

/// <summary>
/// Immutable reference to the origin of an engineering knowledge passage.
/// </summary>
/// <param name="DocumentId">Parent document UUID in SQLite registry.</param>
/// <param name="FileName">Base file name (e.g. "RobotManual.pdf").</param>
/// <param name="RelativePath">Normalized POSIX relative path (e.g. "Manuals/RobotManual.pdf").</param>
/// <param name="Folder">Top-level vault folder (e.g. "Manuals").</param>
/// <param name="PageNumber">1-based page number for PDF manuals, or null for Markdown/Text.</param>
/// <param name="SectionHeading">Nearest Markdown heading section, or null.</param>
public record SourceReference(
    Guid DocumentId,
    string FileName,
    string RelativePath,
    string Folder,
    int? PageNumber = null,
    string? SectionHeading = null
);
