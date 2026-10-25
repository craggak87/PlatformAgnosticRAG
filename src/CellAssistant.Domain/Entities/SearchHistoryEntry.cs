namespace CellAssistant.Domain.Entities;

using CellAssistant.Domain.Enums;
using CellAssistant.Domain.ValueObjects;

/// <summary>
/// Audit trail entry of a user search query, AI answer, and citations stored in SQLite.
/// </summary>
public class SearchHistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public ConfidenceLevel Confidence { get; set; } = ConfidenceLevel.Low;
    public List<SourceReference> Sources { get; set; } = new();
    public DateTimeOffset AskedUtc { get; set; } = DateTimeOffset.UtcNow;
}
