namespace CellAssistant.Application.Ports;

using CellAssistant.Domain.Entities;

public interface ISearchHistoryRepository
{
    /// <summary>
    /// Appends a new query, answer, confidence level, and source citations to the search history.
    /// </summary>
    Task AddAsync(SearchHistoryEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the most recent query entries ordered by asked timestamp descending.
    /// </summary>
    Task<IReadOnlyList<SearchHistoryEntry>> GetRecentAsync(int limit, CancellationToken cancellationToken = default);
}
