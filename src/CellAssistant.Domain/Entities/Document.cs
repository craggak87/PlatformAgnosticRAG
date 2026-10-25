namespace CellAssistant.Domain.Entities;

using CellAssistant.Domain.Enums;

/// <summary>
/// Represents a tracked source file in the local SQLite metadata registry.
/// </summary>
public class Document
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;        // Canonical OS absolute path
    public string RelativePath { get; set; } = string.Empty;    // Normalized POSIX path (forward slashes '/')
    public DocumentType DocumentType { get; set; }
    public string Folder { get; set; } = string.Empty;          // Top-level folder (e.g. "Faults")
    public string ContentHash { get; set; } = string.Empty;     // Lowercase hex SHA-256 of raw file bytes
    public string? EquipmentId { get; set; }                    // From YAML frontmatter `equipment_id`
    public string? Manufacturer { get; set; }                   // From YAML frontmatter `manufacturer`
    public string? Criticality { get; set; }                    // From YAML frontmatter `criticality`
    public List<string> Tags { get; set; } = new();             // Lowercase tags without leading '#'
    public int ChunkCount { get; set; } = 0;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset IndexedUtc { get; set; } = DateTimeOffset.UtcNow;
}
