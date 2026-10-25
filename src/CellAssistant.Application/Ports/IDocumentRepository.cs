namespace CellAssistant.Application.Ports;

using CellAssistant.Domain.Entities;

public interface IDocumentRepository
{
    /// <summary>
    /// Inserts or updates a document record in SQLite.
    /// </summary>
    Task UpsertAsync(Document document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves document metadata by its primary key UUID.
    /// </summary>
    Task<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves document metadata by its canonical OS file path.
    /// </summary>
    Task<Document?> GetByFilePathAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all tracked documents for startup hash reconciliation.
    /// </summary>
    Task<IReadOnlyList<Document>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a document record from SQLite by its UUID.
    /// </summary>
    Task DeleteByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the total count of indexed documents.
    /// </summary>
    Task<int> GetCountAsync(CancellationToken cancellationToken = default);
}
