namespace CellAssistant.Domain.Entities;

/// <summary>
/// Represents an individual text segment extracted and context-prefixed from a Document.
/// </summary>
public class DocumentChunk
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public string Content { get; set; } = string.Empty;         // Context-prefixed chunk text
    public int ChunkIndex { get; set; }                         // 0-based index within document
    public int? PageNumber { get; set; }                        // 1-based page number (for PDFs)
    public string? SectionHeading { get; set; }                 // Heading context (e.g. "Fault History")
    public string? EquipmentId { get; set; }
    public string? FaultId { get; set; }
    public string? AlarmId { get; set; }
    public List<string> Tags { get; set; } = new();
}
