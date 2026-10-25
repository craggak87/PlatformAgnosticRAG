namespace CellAssistant.Application.Ports;

public interface IEmbeddingService
{
    /// <summary>
    /// Generates a single 768-dimensional embedding vector for query or text chunk.
    /// </summary>
    Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates batch embeddings for multiple chunks in a single call.
    /// </summary>
    Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchEmbeddingsAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
}
