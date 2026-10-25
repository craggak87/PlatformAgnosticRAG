namespace CellAssistant.Application.DTOs;

using CellAssistant.Domain.Entities;

/// <summary>
/// Parsed document result containing metadata and extracted context-enriched chunks.
/// </summary>
public record ParsedDocumentResult(
    Document DocumentMetadata,
    IReadOnlyList<DocumentChunk> Chunks
)
{
    public void Deconstruct(out Document document, out IReadOnlyList<DocumentChunk> chunks)
    {
        document = DocumentMetadata;
        chunks = Chunks;
    }
}
