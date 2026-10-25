namespace CellAssistant.Infrastructure.Services;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Application.DTOs;
using CellAssistant.Application.Options;
using CellAssistant.Application.Ports;
using CellAssistant.Domain.Enums;
using CellAssistant.Infrastructure.Persistence.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class HealthCheckService : IHealthCheckService
{
    private readonly ISqliteConnectionFactory _sqliteConnectionFactory;
    private readonly IVectorStore _vectorStore;
    private readonly IIndexerStateRepository _indexerStateRepository;
    private readonly HttpClient _httpClient;
    private readonly KnowledgeBaseOptions _kbOptions;
    private readonly QdrantOptions _qdrantOptions;
    private readonly OllamaOptions _ollamaOptions;
    private readonly ILogger<HealthCheckService> _logger;

    public HealthCheckService(
        ISqliteConnectionFactory sqliteConnectionFactory,
        IVectorStore vectorStore,
        IIndexerStateRepository indexerStateRepository,
        HttpClient httpClient,
        IOptions<KnowledgeBaseOptions> kbOptions,
        IOptions<QdrantOptions> qdrantOptions,
        IOptions<OllamaOptions> ollamaOptions,
        ILogger<HealthCheckService> logger)
    {
        _sqliteConnectionFactory = sqliteConnectionFactory ?? throw new ArgumentNullException(nameof(sqliteConnectionFactory));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _indexerStateRepository = indexerStateRepository ?? throw new ArgumentNullException(nameof(indexerStateRepository));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _kbOptions = kbOptions?.Value ?? throw new ArgumentNullException(nameof(kbOptions));
        _qdrantOptions = qdrantOptions?.Value ?? throw new ArgumentNullException(nameof(qdrantOptions));
        _ollamaOptions = ollamaOptions?.Value ?? throw new ArgumentNullException(nameof(ollamaOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HealthDto> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var components = new Dictionary<string, ComponentHealthDto>(StringComparer.OrdinalIgnoreCase);

        // 1. Probe API
        components["api"] = new ComponentHealthDto("Healthy", "API host running");

        // 2. Probe SQLite
        try
        {
            using var conn = await _sqliteConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM documents;";
            var docCount = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            components["sqlite"] = new ComponentHealthDto("Healthy",
                $"SQLite WAL connected ({_kbOptions.SqlitePath}, {docCount} docs / tracked documents)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health probe failed for SQLite.");
            components["sqlite"] = new ComponentHealthDto("Unhealthy", $"SQLite connection failed: {ex.Message}");
        }

        // 3. Probe Qdrant
        try
        {
            long vectorCount = await _vectorStore.GetVectorCountAsync(cancellationToken);
            components["qdrant"] = new ComponentHealthDto("Healthy",
                $"Qdrant reachable (collection '{_qdrantOptions.CollectionName}' ready, {vectorCount} vectors)");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Health probe failed for Qdrant.");
            components["qdrant"] = new ComponentHealthDto("Unhealthy", $"Qdrant unreachable: {ex.Message}");
        }

        // 4. Probe Ollama
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            var ollamaPingUri = new Uri(new Uri(_ollamaOptions.BaseUrl), "/api/tags");
            using var response = await _httpClient.GetAsync(ollamaPingUri, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                components["ollama"] = new ComponentHealthDto("Healthy",
                    $"Ollama reachable at {_ollamaOptions.BaseUrl} (models present)");
            }
            else
            {
                components["ollama"] = new ComponentHealthDto("Unhealthy",
                    $"Ollama returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Health probe failed for Ollama.");
            components["ollama"] = new ComponentHealthDto("Unhealthy",
                $"Ollama unreachable at {_ollamaOptions.BaseUrl}: {ex.Message}");
        }

        // 5. Probe Indexer
        try
        {
            var state = await _indexerStateRepository.GetStateAsync(cancellationToken);
            string statusStr = state.Status switch
            {
                IndexerStatus.Failed => "Unhealthy",
                IndexerStatus.Degraded => "Degraded",
                _ => "Healthy"
            };

            string desc = state.Status switch
            {
                IndexerStatus.Failed => $"Indexer failed: {state.LastError}",
                IndexerStatus.Degraded => $"Indexer degraded: {state.LastError}",
                IndexerStatus.Reindexing => "Indexer actively reindexing vault",
                IndexerStatus.Indexing => "Indexer processing change events",
                _ => $"Watcher active, last run status: Success ({state.LastRunUtc:u})"
            };

            components["indexer"] = new ComponentHealthDto(statusStr, desc);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Health probe failed for Indexer.");
            components["indexer"] = new ComponentHealthDto("Unhealthy", $"Indexer state lookup failed: {ex.Message}");
        }

        // Aggregate Overall Status
        bool hasUnhealthy = false;
        bool hasDegraded = false;

        foreach (var kvp in components)
        {
            if (kvp.Value.Status.Equals("Unhealthy", StringComparison.OrdinalIgnoreCase))
            {
                hasUnhealthy = true;
            }
            else if (kvp.Value.Status.Equals("Degraded", StringComparison.OrdinalIgnoreCase))
            {
                hasDegraded = true;
            }
        }

        string overallStatus = hasUnhealthy ? "Unhealthy" : (hasDegraded ? "Degraded" : "Healthy");

        return new HealthDto(overallStatus, components);
    }
}
