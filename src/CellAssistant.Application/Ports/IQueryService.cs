namespace CellAssistant.Application.Ports;

using CellAssistant.Application.DTOs;

public interface IQueryService
{
    /// <summary>
    /// Executes a synchronous query, orchestrating retrieval, confidence calculation, context assembly, and LLM answer generation.
    /// </summary>
    Task<QueryResponseDto> ExecuteQueryAsync(
        QueryRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a streaming query, emitting metadata first, followed by real-time tokens via SSE.
    /// </summary>
    Task ExecuteQueryStreamAsync(
        QueryRequestDto request,
        Func<string, Task> onToken,
        Func<QueryMetadataDto, Task> onMetadata,
        CancellationToken cancellationToken = default);
}
