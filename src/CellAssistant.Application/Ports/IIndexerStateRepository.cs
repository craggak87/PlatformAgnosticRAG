namespace CellAssistant.Application.Ports;

using CellAssistant.Domain.Entities;
using CellAssistant.Domain.Enums;

public interface IIndexerStateRepository
{
    /// <summary>
    /// Gets the current worker state.
    /// </summary>
    Task<IndexerState> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the worker state record.
    /// </summary>
    Task UpdateStateAsync(IndexerState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the indexer status and optional error message.
    /// </summary>
    Task UpdateStatusAsync(IndexerStatus status, string? lastError = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the reindex_requested flag (e.g. from POST /api/admin/reindex).
    /// </summary>
    Task SetReindexRequestedAsync(bool requested, CancellationToken cancellationToken = default);
}
