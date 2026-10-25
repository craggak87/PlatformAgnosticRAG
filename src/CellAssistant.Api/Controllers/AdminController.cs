namespace CellAssistant.Api.Controllers;

using System;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Application.DTOs;
using CellAssistant.Application.Ports;
using CellAssistant.Domain.Entities;
using CellAssistant.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

[ApiController]
[Route("api/admin")]
[Produces("application/json")]
public class AdminController : ControllerBase
{
    private readonly IIndexerStateRepository _indexerStateRepository;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        IIndexerStateRepository indexerStateRepository,
        IServiceScopeFactory scopeFactory,
        ILogger<AdminController> logger)
    {
        _indexerStateRepository = indexerStateRepository ?? throw new ArgumentNullException(nameof(indexerStateRepository));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Triggers an asynchronous vault scan and SHA-256 hash reconciliation job.
    /// Returns HTTP 202 Accepted immediately.
    /// </summary>
    [HttpPost("reindex")]
    [ProducesResponseType(typeof(ReindexResponseDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> TriggerReindexAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Admin reindex requested via POST /api/admin/reindex.");

        // 1. Mark reindex requested flag in SQLite
        await _indexerStateRepository.SetReindexRequestedAsync(true, cancellationToken);

        // 2. Dispatch background indexing execution without blocking the HTTP thread
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var stateRepo = scope.ServiceProvider.GetRequiredService<IIndexerStateRepository>();
            var indexer = scope.ServiceProvider.GetRequiredService<IDocumentIndexer>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<AdminController>>();

            try
            {
                logger.LogInformation("Starting background vault reindexing execution...");
                await stateRepo.UpdateStatusAsync(IndexerStatus.Reindexing);

                var result = await indexer.IndexAllAsync(CancellationToken.None);

                logger.LogInformation(
                    "Background reindexing finished in {Duration}ms. Scanned: {Scanned}, Indexed: {Indexed}, Errors: {Errors}",
                    result.Duration.TotalMilliseconds, result.TotalFilesScanned, result.FilesIndexed, result.ErrorsCount);

                var finalStatus = result.ErrorsCount > 0 ? IndexerStatus.Degraded : IndexerStatus.Idle;
                string? finalError = result.ErrorsCount > 0 ? $"{result.ErrorsCount} errors during reindexing" : null;

                await stateRepo.UpdateStateAsync(new IndexerState
                {
                    Id = 1,
                    Status = finalStatus,
                    LastRunUtc = DateTimeOffset.UtcNow,
                    LastError = finalError,
                    ReindexRequested = false
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background reindexing failed with unhandled exception.");
                await stateRepo.UpdateStateAsync(new IndexerState
                {
                    Id = 1,
                    Status = IndexerStatus.Failed,
                    LastRunUtc = DateTimeOffset.UtcNow,
                    LastError = ex.Message,
                    ReindexRequested = false
                });
            }
        });

        // 3. Return 202 Accepted
        var response = new ReindexResponseDto(
            status: "Reindex requested successfully",
            reindexRequested: true,
            timestamp: DateTimeOffset.UtcNow,
            triggeredUtc: DateTime.UtcNow
        );

        return StatusCode(StatusCodes.Status202Accepted, response);
    }
}
