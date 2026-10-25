namespace CellAssistant.Application.DTOs;

/// <summary>
/// Execution summary returned by IDocumentIndexer.IndexAllAsync.
/// </summary>
public record IndexerRunResult(
    int TotalFilesScanned,
    int FilesIndexed,
    int FilesSkippedUnchanged,
    int FilesDeleted,
    int ErrorsCount,
    TimeSpan Duration,
    bool Success
);
