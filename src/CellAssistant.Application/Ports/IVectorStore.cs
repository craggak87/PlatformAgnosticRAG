namespace CellAssistant.Application.Ports;

using CellAssistant.Domain.Entities;
using CellAssistant.Domain.ValueObjects;

public interface IVectorStore
{
    /// <summary>
    /// Ensures the 'cell-knowledge' collection exists in Qdrant with Cosine distance,
    /// 768 vector dimensions, and all required payload indexes.
    /// </summary>
    Task InitializeCollectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts a batch of vectorized chunks and their metadata payload into Qdrant.
    /// </summary>
    Task UpsertVectorsAsync(IReadOnlyList<VectorPoint> points, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all vector points associated with a specific Document ID.
    /// </summary>
    Task DeleteByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes hybrid retrieval combining dense vector similarity with metadata filtering.
    /// </summary>
    Task<IReadOnlyList<RetrievedChunk>> SearchHybridAsync(
        ReadOnlyMemory<float> queryVector,
        string? equipmentId,
        string? folder,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Overload supporting explicit exact keyword matching tokens for hybrid payload search.
    /// </summary>
    Task<IReadOnlyList<RetrievedChunk>> SearchHybridAsync(
        ReadOnlyMemory<float> queryVector,
        IReadOnlyList<string>? exactKeywords,
        string? equipmentId,
        string? folder,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the total number of indexed vector points in the collection.
    /// </summary>
    Task<long> GetVectorCountAsync(CancellationToken cancellationToken = default);
}
