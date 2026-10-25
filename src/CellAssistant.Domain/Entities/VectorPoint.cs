namespace CellAssistant.Domain.Entities;

/// <summary>
/// Combines a text chunk, its embedding vector, and searchable metadata payload for Qdrant.
/// </summary>
public class VectorPoint
{
    public Guid ChunkId { get; set; } = Guid.NewGuid();        // Point ID in Qdrant collection
    public Guid DocumentId { get; set; }
    public float[] Vector { get; set; } = Array.Empty<float>();// Float32 embedding vector (dimension 768)
    public string Content { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;
    public int? PageNumber { get; set; }
    public string? SectionHeading { get; set; }
    public string? EquipmentId { get; set; }
    public string? FaultId { get; set; }
    public string? AlarmId { get; set; }
    public List<string> Tags { get; set; } = new();
}
