namespace CellAssistant.Infrastructure.Indexing;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Application.DTOs;
using CellAssistant.Application.Options;
using CellAssistant.Application.Ports;
using CellAssistant.Domain.Entities;
using CellAssistant.Domain.Enums;
using CellAssistant.Infrastructure.Parsing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Implements full vault scanning, SHA-256 hash reconciliation, incremental document indexing,
/// and vector cleanup between the filesystem, SQLite metadata registry, and Qdrant.
/// </summary>
public class DocumentIndexer : IDocumentIndexer
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md",
        ".pdf",
        ".txt"
    };

    private readonly IEnumerable<IDocumentParser> _parsers;
    private readonly IDocumentRepository _documentRepository;
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly IIndexerStateRepository _indexerStateRepository;
    private readonly KnowledgeBaseOptions _options;
    private readonly ILogger<DocumentIndexer> _logger;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    public DocumentIndexer(
        IEnumerable<IDocumentParser> parsers,
        IDocumentRepository documentRepository,
        IVectorStore vectorStore,
        IEmbeddingService embeddingService,
        IIndexerStateRepository indexerStateRepository,
        IOptions<KnowledgeBaseOptions> options,
        ILogger<DocumentIndexer> logger)
    {
        _parsers = parsers ?? throw new ArgumentNullException(nameof(parsers));
        _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _indexerStateRepository = indexerStateRepository ?? throw new ArgumentNullException(nameof(indexerStateRepository));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IndexerRunResult> IndexAllAsync(CancellationToken cancellationToken = default)
    {
        await _syncLock.WaitAsync(cancellationToken);
        var sw = Stopwatch.StartNew();

        int totalScanned = 0;
        int filesIndexed = 0;
        int filesSkippedUnchanged = 0;
        int filesDeleted = 0;
        int errorsCount = 0;

        try
        {
            _logger.LogInformation("Starting vault indexing reconciliation. RootPath: '{RootPath}'", _options.RootPath);
            await _indexerStateRepository.UpdateStatusAsync(IndexerStatus.Indexing, null, cancellationToken);

            string rootDirectory = Path.GetFullPath(_options.RootPath);
            if (!Directory.Exists(rootDirectory))
            {
                _logger.LogWarning("KnowledgeBase RootPath does not exist: '{RootDirectory}'", rootDirectory);
                sw.Stop();
                await _indexerStateRepository.UpdateStateAsync(new IndexerState
                {
                    Status = IndexerStatus.Idle,
                    LastRunUtc = DateTimeOffset.UtcNow,
                    LastError = null,
                    ReindexRequested = false
                }, cancellationToken);

                return new IndexerRunResult(0, 0, 0, 0, 0, sw.Elapsed, true);
            }

            // 1. Fetch all currently tracked documents from SQLite
            var existingDocs = await _documentRepository.GetAllAsync(cancellationToken);
            var existingByPath = existingDocs.ToDictionary(
                d => Path.GetFullPath(d.FilePath),
                StringComparer.OrdinalIgnoreCase);

            var discoveredFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 2. Discover files recursively across the vault
            var allDiskFiles = Directory.EnumerateFiles(rootDirectory, "*.*", SearchOption.AllDirectories);

            foreach (var diskFile in allDiskFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fullPath = Path.GetFullPath(diskFile);

                if (ShouldIgnore(fullPath, rootDirectory))
                {
                    continue;
                }

                var ext = Path.GetExtension(fullPath);
                if (!SupportedExtensions.Contains(ext))
                {
                    continue;
                }

                discoveredFilePaths.Add(fullPath);
                totalScanned++;

                try
                {
                    var parser = _parsers.FirstOrDefault(p => p.CanParse(fullPath));
                    if (parser == null)
                    {
                        _logger.LogWarning("No parser available for supported file '{FullPath}'", fullPath);
                        continue;
                    }

                    string currentHash = await ComputeSha256Async(fullPath, cancellationToken);

                    if (existingByPath.TryGetValue(fullPath, out var existingDoc))
                    {
                        if (string.Equals(existingDoc.ContentHash, currentHash, StringComparison.OrdinalIgnoreCase))
                        {
                            filesSkippedUnchanged++;
                            continue;
                        }

                        // Hash changed -> reindex
                        await IndexSingleFileInternalAsync(fullPath, rootDirectory, parser, currentHash, existingDoc, cancellationToken);
                        filesIndexed++;
                    }
                    else
                    {
                        // New file -> index
                        await IndexSingleFileInternalAsync(fullPath, rootDirectory, parser, currentHash, null, cancellationToken);
                        filesIndexed++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errorsCount++;
                    _logger.LogError(ex, "Failed to index file '{FullPath}' during IndexAllAsync.", fullPath);
                }
            }

            // 3. Reconcile deleted files (tracked in SQLite but missing from disk)
            foreach (var existingDoc in existingDocs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fullPath = Path.GetFullPath(existingDoc.FilePath);

                if (!discoveredFilePaths.Contains(fullPath) && !File.Exists(fullPath))
                {
                    try
                    {
                        _logger.LogInformation("Pruning deleted document '{FilePath}' (ID: {Id})",
                            existingDoc.FilePath, existingDoc.Id);

                        await _vectorStore.DeleteByDocumentIdAsync(existingDoc.Id, cancellationToken);
                        await _documentRepository.DeleteByIdAsync(existingDoc.Id, cancellationToken);
                        filesDeleted++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        errorsCount++;
                        _logger.LogError(ex, "Failed to prune deleted document '{FilePath}' (ID: {Id}).",
                            existingDoc.FilePath, existingDoc.Id);
                    }
                }
            }

            sw.Stop();
            bool success = errorsCount == 0;
            var finalStatus = success ? IndexerStatus.Idle : (errorsCount < totalScanned ? IndexerStatus.Degraded : IndexerStatus.Failed);
            string? lastError = errorsCount > 0 ? $"{errorsCount} errors occurred during indexing run." : null;

            await _indexerStateRepository.UpdateStateAsync(new IndexerState
            {
                Status = finalStatus,
                LastRunUtc = DateTimeOffset.UtcNow,
                LastError = lastError,
                ReindexRequested = false
            }, cancellationToken);

            _logger.LogInformation(
                "IndexAllAsync completed in {ElapsedMs}ms. Scanned: {Scanned}, Indexed: {Indexed}, Skipped: {Skipped}, Deleted: {Deleted}, Errors: {Errors}",
                sw.ElapsedMilliseconds, totalScanned, filesIndexed, filesSkippedUnchanged, filesDeleted, errorsCount);

            return new IndexerRunResult(
                TotalFilesScanned: totalScanned,
                FilesIndexed: filesIndexed,
                FilesSkippedUnchanged: filesSkippedUnchanged,
                FilesDeleted: filesDeleted,
                ErrorsCount: errorsCount,
                Duration: sw.Elapsed,
                Success: success);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("IndexAllAsync operation was cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            errorsCount++;
            _logger.LogError(ex, "Catastrophic error during IndexAllAsync execution.");

            await _indexerStateRepository.UpdateStateAsync(new IndexerState
            {
                Status = IndexerStatus.Failed,
                LastRunUtc = DateTimeOffset.UtcNow,
                LastError = ex.Message,
                ReindexRequested = false
            }, CancellationToken.None);

            return new IndexerRunResult(
                TotalFilesScanned: totalScanned,
                FilesIndexed: filesIndexed,
                FilesSkippedUnchanged: filesSkippedUnchanged,
                FilesDeleted: filesDeleted,
                ErrorsCount: errorsCount,
                Duration: sw.Elapsed,
                Success: false);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task<bool> IndexFileAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            string rootDirectory = Path.GetFullPath(_options.RootPath);
            fullPath = Path.GetFullPath(fullPath);

            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("IndexFileAsync: Target file does not exist at '{FullPath}'", fullPath);
                return false;
            }

            if (ShouldIgnore(fullPath, rootDirectory))
            {
                _logger.LogDebug("IndexFileAsync: Skipping ignored file '{FullPath}'", fullPath);
                return false;
            }

            var ext = Path.GetExtension(fullPath);
            if (!SupportedExtensions.Contains(ext))
            {
                _logger.LogDebug("IndexFileAsync: Unsupported extension '{Extension}' for file '{FullPath}'", ext, fullPath);
                return false;
            }

            var parser = _parsers.FirstOrDefault(p => p.CanParse(fullPath));
            if (parser == null)
            {
                _logger.LogWarning("IndexFileAsync: No parser found for file '{FullPath}'", fullPath);
                return false;
            }

            string currentHash = await ComputeSha256Async(fullPath, cancellationToken);
            var existingDoc = await _documentRepository.GetByFilePathAsync(fullPath, cancellationToken);

            if (existingDoc != null && string.Equals(existingDoc.ContentHash, currentHash, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("IndexFileAsync: File '{FullPath}' content hash unchanged ({Hash}). Skipping.", fullPath, currentHash);
                return false;
            }

            await IndexSingleFileInternalAsync(fullPath, rootDirectory, parser, currentHash, existingDoc, cancellationToken);
            return true;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task<bool> RemoveFileAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            fullPath = Path.GetFullPath(fullPath);
            var existingDoc = await _documentRepository.GetByFilePathAsync(fullPath, cancellationToken);

            if (existingDoc == null)
            {
                _logger.LogInformation("RemoveFileAsync: Document with path '{FullPath}' not found in SQLite registry.", fullPath);
                return false;
            }

            _logger.LogInformation("RemoveFileAsync: Removing document '{FullPath}' (ID: {Id}) from Qdrant and SQLite.",
                fullPath, existingDoc.Id);

            await _vectorStore.DeleteByDocumentIdAsync(existingDoc.Id, cancellationToken);
            await _documentRepository.DeleteByIdAsync(existingDoc.Id, cancellationToken);

            return true;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task IndexSingleFileInternalAsync(
        string fullPath,
        string rootDirectory,
        IDocumentParser parser,
        string contentHash,
        Document? existingDoc,
        CancellationToken cancellationToken)
    {
        var relativePath = ChunkContextEnricher.NormalizeRelativePath(Path.GetRelativePath(rootDirectory, fullPath));

        // 1. Parse document content and extract context-enriched chunks
        var (parsedDoc, chunks) = await parser.ParseAsync(fullPath, relativePath, cancellationToken);

        // 2. Synchronize identity and timestamps
        if (existingDoc != null)
        {
            parsedDoc.Id = existingDoc.Id;
            parsedDoc.CreatedUtc = existingDoc.CreatedUtc;
        }
        else
        {
            if (parsedDoc.Id == Guid.Empty)
            {
                parsedDoc.Id = Guid.NewGuid();
            }
            parsedDoc.CreatedUtc = DateTimeOffset.UtcNow;
        }

        parsedDoc.FilePath = fullPath;
        parsedDoc.RelativePath = relativePath;
        parsedDoc.ContentHash = contentHash;
        parsedDoc.FileName = Path.GetFileName(fullPath);

        if (string.IsNullOrWhiteSpace(parsedDoc.Folder))
        {
            parsedDoc.Folder = ChunkContextEnricher.ExtractFolder(relativePath, "General");
        }

        parsedDoc.ChunkCount = chunks?.Count ?? 0;
        parsedDoc.ModifiedUtc = File.Exists(fullPath) ? File.GetLastWriteTimeUtc(fullPath) : DateTimeOffset.UtcNow;
        parsedDoc.IndexedUtc = DateTimeOffset.UtcNow;

        // 3. Purge existing vectors for this document UUID in Qdrant
        await _vectorStore.DeleteByDocumentIdAsync(parsedDoc.Id, cancellationToken);

        // 4. Batch embed and upsert vectors if chunks are present
        if (chunks != null && chunks.Count > 0)
        {
            var chunkTexts = chunks.Select(c => c.Content).ToList();
            var embeddings = await _embeddingService.GenerateBatchEmbeddingsAsync(chunkTexts, cancellationToken);

            if (embeddings.Count != chunks.Count)
            {
                throw new InvalidOperationException(
                    $"Embedding count mismatch for '{fullPath}': {chunks.Count} chunks vs {embeddings.Count} embeddings.");
            }

            var vectorPoints = new List<VectorPoint>(chunks.Count);
            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                var embedding = embeddings[i];

                var point = new VectorPoint
                {
                    ChunkId = chunk.Id == Guid.Empty ? Guid.NewGuid() : chunk.Id,
                    DocumentId = parsedDoc.Id,
                    Vector = embedding.ToArray(),
                    Content = chunk.Content,
                    FileName = parsedDoc.FileName,
                    RelativePath = parsedDoc.RelativePath,
                    Folder = parsedDoc.Folder,
                    PageNumber = chunk.PageNumber,
                    SectionHeading = chunk.SectionHeading,
                    EquipmentId = !string.IsNullOrEmpty(chunk.EquipmentId) ? chunk.EquipmentId : parsedDoc.EquipmentId,
                    FaultId = chunk.FaultId,
                    AlarmId = chunk.AlarmId,
                    Tags = (chunk.Tags != null && chunk.Tags.Count > 0) ? chunk.Tags : parsedDoc.Tags
                };

                vectorPoints.Add(point);
            }

            await _vectorStore.UpsertVectorsAsync(vectorPoints, cancellationToken);
        }

        // 5. Upsert document metadata record into SQLite
        await _documentRepository.UpsertAsync(parsedDoc, cancellationToken);

        _logger.LogInformation("Indexed document '{RelativePath}' ({ChunkCount} chunks, Hash: {Hash})",
            parsedDoc.RelativePath, parsedDoc.ChunkCount, parsedDoc.ContentHash);
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 8192, useAsync: true);
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static bool ShouldIgnore(string fullPath, string rootPath)
    {
        var fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(fileName)) return true;

        // Ignore hidden dot files
        if (fileName.StartsWith('.')) return true;

        // Ignore temporary editor files
        if (fileName.StartsWith('~') || fileName.EndsWith('~') || fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            return true;

        var relativePath = Path.GetRelativePath(rootPath, fullPath);
        var segments = relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);

        // Check if any ancestor directory starts with '.' (e.g. .obsidian, .git)
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (segments[i].StartsWith('.'))
                return true;
        }

        return false;
    }
}
