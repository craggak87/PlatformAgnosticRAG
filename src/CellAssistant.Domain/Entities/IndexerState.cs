namespace CellAssistant.Domain.Entities;

using CellAssistant.Domain.Enums;

/// <summary>
/// Singleton entity representing the operational state of the indexing coordinator in SQLite.
/// </summary>
public class IndexerState
{
    public int Id { get; set; } = 1;                           // Primary key check (id = 1)
    public IndexerStatus Status { get; set; } = IndexerStatus.Idle;
    public DateTimeOffset? LastRunUtc { get; set; }
    public string? LastError { get; set; }
    public bool ReindexRequested { get; set; } = false;
}
