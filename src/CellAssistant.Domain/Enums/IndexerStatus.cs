namespace CellAssistant.Domain.Enums;

/// <summary>
/// Operational status of the background file indexing coordinator.
/// </summary>
public enum IndexerStatus
{
    Idle,
    Indexing,
    Reindexing,
    Reconciling,
    Failed,
    Degraded
}
