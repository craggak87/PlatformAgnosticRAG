namespace CellAssistant.Domain.ValueObjects;

/// <summary>
/// Encapsulates a text chunk retrieved during hybrid search with similarity score and exact match metadata.
/// </summary>
/// <param name="Content">Context-prefixed text of the chunk.</param>
/// <param name="SimilarityScore">Cosine similarity score (0.0 to 1.0).</param>
/// <param name="ExactIdentifierMatch">True if chunk matches an exact query identifier (e.g. ALM-205, I:0/15).</param>
/// <param name="Source">The source provenance of this chunk.</param>
public record RetrievedChunk(
    string Content,
    double SimilarityScore,
    bool ExactIdentifierMatch,
    SourceReference Source
);
