namespace CellAssistant.Worker;

using System;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Application.Ports;
using CellAssistant.Infrastructure.Persistence.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Hosted background service managing database and vector store initialization,
/// startup vault index reconciliation (IndexAllAsync), and debounced file watcher lifecycle.
/// </summary>
public sealed class IndexerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFileWatcherService _fileWatcherService;
    private readonly ILogger<IndexerWorker> _logger;

    public IndexerWorker(
        IServiceScopeFactory scopeFactory,
        IFileWatcherService fileWatcherService,
        ILogger<IndexerWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _fileWatcherService = fileWatcherService ?? throw new ArgumentNullException(nameof(fileWatcherService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IndexerWorker background daemon started.");

        try
        {
            // 1. Initialise SQLite schema and Qdrant vector collection
            using (var scope = _scopeFactory.CreateScope())
            {
                var sqliteInitializer = scope.ServiceProvider.GetRequiredService<ISqliteDatabaseInitializer>();
                _logger.LogInformation("Initializing SQLite relational database schema (WAL mode)...");
                await sqliteInitializer.InitializeAsync(stoppingToken);

                var vectorStore = scope.ServiceProvider.GetRequiredService<IVectorStore>();
                _logger.LogInformation("Initializing Qdrant vector collection and payload indexes...");
                await vectorStore.InitializeCollectionAsync(stoppingToken);

                // 2. Perform startup SHA-256 hash reconciliation
                _logger.LogInformation("Starting initial vault index reconciliation (IndexAllAsync)...");
                var indexer = scope.ServiceProvider.GetRequiredService<IDocumentIndexer>();
                var result = await indexer.IndexAllAsync(stoppingToken);

                _logger.LogInformation(
                    "Startup reconciliation completed in {Duration}ms. Scanned: {Scanned}, Indexed: {Indexed}, Skipped: {Skipped}, Deleted: {Deleted}, Errors: {Errors}",
                    result.Duration.TotalMilliseconds, result.TotalFilesScanned, result.FilesIndexed, result.FilesSkippedUnchanged, result.FilesDeleted, result.ErrorsCount);
            }

            // 3. Start real-time debounced file watcher
            _logger.LogInformation("Starting IFileWatcherService monitoring...");
            await _fileWatcherService.StartAsync(stoppingToken);

            // 4. Remain active until shutdown requested
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("IndexerWorker received shutdown signal. Stopping services...");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "IndexerWorker encountered an unhandled exception in background execution.");
            throw;
        }
        finally
        {
            await _fileWatcherService.StopAsync(CancellationToken.None);
            _logger.LogInformation("IndexerWorker background daemon shutdown complete.");
        }
    }
}
