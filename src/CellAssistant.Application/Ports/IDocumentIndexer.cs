namespace CellAssistant.Application.Ports;

using CellAssistant.Application.DTOs;

public interface IDocumentIndexer
{
    /// <summary>
    /// Scans the entire knowledge base vault, compares SHA-256 hashes against SQLite,
    /// indexes new/changed documents, and removes deleted documents.
    /// </summary>
    Task<IndexerRunResult> IndexAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Incrementally indexes a single file if its hash has changed.
    /// </summary>
    Task<bool> IndexFileAsync(string fullPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a deleted or renamed file from SQLite and Qdrant.
    /// </summary>
    Task<bool> RemoveFileAsync(string fullPath, CancellationToken cancellationToken = default);
}
