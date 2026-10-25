namespace CellAssistant.Infrastructure.Watcher;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CellAssistant.Application.Options;
using CellAssistant.Application.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Monitors the knowledge base vault for filesystem events across the 16 standard manufacturing folders,
/// debouncing rapid bursts into a 500ms sliding window and executing exponential backoff for locked files.
/// </summary>
public sealed class DebouncedFileWatcherService : IFileWatcherService, IDisposable
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".pdf", ".txt"
    };

    private static readonly HashSet<string> StandardVaultFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Equipment", "Faults", "PLC", "Robot", "Vision", "Safety",
        "Electrical", "Pneumatics", "Mechanical", "Maintenance", "Alarms",
        "Procedures", "Improvements", "Lessons-Learned", "Drawings", "Manuals"
    };

    private readonly string _rootPath;
    private readonly int _debounceMs;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly IDocumentIndexer? _indexerDirect;
    private readonly ILogger<DebouncedFileWatcherService> _logger;

    private readonly ConcurrentDictionary<string, DebounceState> _pendingOperations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Channel<FileAction> _actionChannel;
    private readonly CancellationTokenSource _cts = new();

    private FileSystemWatcher? _watcher;
    private Task? _processingTask;
    private bool _isWatching;
    private bool _disposed;
    private readonly object _stateLock = new();

    [ActivatorUtilitiesConstructor]
    public DebouncedFileWatcherService(
        IOptions<KnowledgeBaseOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<DebouncedFileWatcherService> logger)
        : this(options, scopeFactory, null, logger)
    {
    }

    internal DebouncedFileWatcherService(
        IOptions<KnowledgeBaseOptions> options,
        IDocumentIndexer indexer,
        ILogger<DebouncedFileWatcherService> logger)
        : this(options, null, indexer, logger)
    {
    }

    private DebouncedFileWatcherService(
        IOptions<KnowledgeBaseOptions> options,
        IServiceScopeFactory? scopeFactory,
        IDocumentIndexer? indexerDirect,
        ILogger<DebouncedFileWatcherService> logger)
    {
        ArgumentNullException.ThrowIfNull(options?.Value);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _scopeFactory = scopeFactory;
        _indexerDirect = indexerDirect;

        _rootPath = Path.GetFullPath(options.Value.RootPath);
        _debounceMs = options.Value.WatcherDebounceMs > 0 ? options.Value.WatcherDebounceMs : 500;

        _actionChannel = Channel.CreateUnbounded<FileAction>(new UnboundedChannelOptions
        {
            SingleWriter = false,
            SingleReader = true
        });
    }

    public void StartWatching()
    {
        lock (_stateLock)
        {
            if (_isWatching || _disposed) return;

            if (!Directory.Exists(_rootPath))
            {
                Directory.CreateDirectory(_rootPath);
                _logger.LogInformation("Created knowledge base root directory at '{RootPath}'", _rootPath);
            }

            _watcher = new FileSystemWatcher(_rootPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName
                             | NotifyFilters.DirectoryName
                             | NotifyFilters.LastWrite
                             | NotifyFilters.Size
                             | NotifyFilters.CreationTime,
                InternalBufferSize = 65536
            };

            _watcher.Created += OnFileSystemEvent;
            _watcher.Changed += OnFileSystemEvent;
            _watcher.Deleted += OnFileSystemEvent;
            _watcher.Renamed += OnFileSystemRenamed;
            _watcher.Error += OnFileSystemError;

            _watcher.EnableRaisingEvents = true;
            _isWatching = true;

            _processingTask = Task.Run(() => ProcessActionsAsync(_cts.Token));
            _logger.LogInformation("DebouncedFileWatcher started monitoring '{RootPath}' (Debounce: {DebounceMs}ms)", _rootPath, _debounceMs);
        }
    }

    public void StopWatching()
    {
        lock (_stateLock)
        {
            if (!_isWatching) return;

            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnFileSystemEvent;
                _watcher.Changed -= OnFileSystemEvent;
                _watcher.Deleted -= OnFileSystemEvent;
                _watcher.Renamed -= OnFileSystemRenamed;
                _watcher.Error -= OnFileSystemError;
                _watcher.Dispose();
                _watcher = null;
            }

            foreach (var kvp in _pendingOperations)
            {
                kvp.Value.Dispose();
            }
            _pendingOperations.Clear();

            _isWatching = false;
            _logger.LogInformation("DebouncedFileWatcher stopped monitoring '{RootPath}'", _rootPath);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        StartWatching();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        StopWatching();
        _cts.Cancel();
        if (_processingTask != null)
        {
            try
            {
                await _processingTask.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
            {
                // Graceful cancellation/timeout
            }
        }
    }

    public bool IsPathMonitored(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return false;

        string extension = Path.GetExtension(fullPath);
        if (!SupportedExtensions.Contains(extension)) return false;

        string fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(fileName)) return false;

        // Ignore temporary editor lock/backup files
        if (fileName.StartsWith('.') || fileName.StartsWith('~') || fileName.EndsWith('~') || fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            return false;

        string relativePath;
        try
        {
            relativePath = Path.GetRelativePath(_rootPath, fullPath).Replace('\\', '/');
        }
        catch
        {
            return false;
        }

        if (relativePath.StartsWith("..") || Path.IsPathRooted(relativePath))
            return false;

        var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        // Check for hidden folders or .obsidian anywhere in path
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].StartsWith('.') || parts[i].Equals(".obsidian", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Must be in one of the 16 vault folders or at vault root
        if (parts.Length > 1 && !StandardVaultFolders.Contains(parts[0]))
        {
            return false;
        }

        return true;
    }

    internal void ProcessRawEvent(WatcherChangeTypes changeType, string fullPath, string? oldFullPath = null, TaskCompletionSource<bool>? tcs = null)
    {
        if (changeType == WatcherChangeTypes.Renamed)
        {
            // Handle OldFullPath deletion if old path was monitored
            if (!string.IsNullOrEmpty(oldFullPath) && IsPathMonitored(oldFullPath))
            {
                ScheduleDebounce(oldFullPath, WatcherChangeTypes.Deleted, null, null);
            }

            // Handle FullPath indexing if new path is monitored
            if (IsPathMonitored(fullPath))
            {
                ScheduleDebounce(fullPath, WatcherChangeTypes.Created, null, tcs);
            }
            else
            {
                tcs?.TrySetResult(false);
            }
            return;
        }

        if (!IsPathMonitored(fullPath))
        {
            tcs?.TrySetResult(false);
            return;
        }

        ScheduleDebounce(fullPath, changeType, oldFullPath, tcs);
    }

    private void ScheduleDebounce(string fullPath, WatcherChangeTypes changeType, string? oldFullPath, TaskCompletionSource<bool>? tcs)
    {
        _pendingOperations.AddOrUpdate(
            fullPath,
            addValueFactory: key => new DebounceState(key, changeType, oldFullPath, _debounceMs, OnTimerElapsed, tcs),
            updateValueFactory: (key, existing) =>
            {
                existing.Reset(changeType, oldFullPath, _debounceMs, tcs);
                return existing;
            });
    }

    private void OnTimerElapsed(object? state)
    {
        if (state is not DebounceState item) return;

        if (_pendingOperations.TryRemove(item.FullPath, out var removedItem))
        {
            var action = new FileAction(
                removedItem.FullPath,
                removedItem.LastChangeType,
                removedItem.OldFullPath,
                removedItem.CompletionSource);

            removedItem.Dispose();
            _actionChannel.Writer.TryWrite(action);
        }
    }

    private async Task ProcessActionsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && await _actionChannel.Reader.WaitToReadAsync(ct))
        {
            while (_actionChannel.Reader.TryRead(out var action))
            {
                try
                {
                    await ExecuteActionWithRetryAsync(action, ct);
                    action.CompletionSource?.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed executing debounced file action for '{FilePath}'", action.FullPath);
                    action.CompletionSource?.TrySetException(ex);
                }
            }
        }
    }

    private async Task ExecuteActionWithRetryAsync(FileAction action, CancellationToken ct)
    {
        if (action.ChangeType == WatcherChangeTypes.Deleted)
        {
            _logger.LogInformation("Processing debounced deletion: '{FilePath}'", action.FullPath);
            await ExecuteIndexerCommandAsync(indexer => indexer.RemoveFileAsync(action.FullPath, ct));
            return;
        }

        // For Created and Changed: wait for file lock release with exponential backoff
        bool isAvailable = await WaitForFileAvailableAsync(action.FullPath, maxRetries: 5, initialDelayMs: 100, _logger, ct);
        if (!isAvailable)
        {
            _logger.LogWarning("File '{FilePath}' was inaccessible or removed after debounce window. Skipping.", action.FullPath);
            return;
        }

        _logger.LogInformation("Processing debounced index: '{FilePath}' ({ChangeType})", action.FullPath, action.ChangeType);
        await ExecuteIndexerCommandAsync(indexer => indexer.IndexFileAsync(action.FullPath, ct));
    }

    private async Task ExecuteIndexerCommandAsync(Func<IDocumentIndexer, Task> action)
    {
        if (_indexerDirect != null)
        {
            await action(_indexerDirect);
            return;
        }

        if (_scopeFactory != null)
        {
            using var scope = _scopeFactory.CreateScope();
            var indexer = scope.ServiceProvider.GetRequiredService<IDocumentIndexer>();
            await action(indexer);
            return;
        }

        throw new InvalidOperationException("Neither IDocumentIndexer nor IServiceScopeFactory is configured.");
    }

    public static async Task<bool> WaitForFileAvailableAsync(
        string filePath,
        int maxRetries = 5,
        int initialDelayMs = 100,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return false;
                }

                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length >= 0)
                {
                    return true;
                }
            }
            catch (IOException ex)
            {
                int delayMs = initialDelayMs * (int)Math.Pow(2, attempt - 1);
                logger?.LogWarning(ex,
                    "File '{FilePath}' is locked by another process (attempt {Attempt}/{MaxRetries}). Retrying in {DelayMs}ms.",
                    filePath, attempt, maxRetries, delayMs);

                if (attempt == maxRetries)
                {
                    logger?.LogError(ex, "File '{FilePath}' remained locked after {MaxRetries} retries.", filePath, maxRetries);
                    return false;
                }

                await Task.Delay(delayMs, ct);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger?.LogError(ex, "Access denied reading '{FilePath}'.", filePath);
                return false;
            }
        }

        return false;
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e) =>
        ProcessRawEvent(e.ChangeType, e.FullPath);

    private void OnFileSystemRenamed(object sender, RenamedEventArgs e) =>
        ProcessRawEvent(e.ChangeType, e.FullPath, e.OldFullPath);

    private void OnFileSystemError(object sender, ErrorEventArgs e) =>
        _logger.LogError(e.GetException(), "FileSystemWatcher buffer overflow or critical error encountered in '{RootPath}'", _rootPath);

    public void Dispose()
    {
        lock (_stateLock)
        {
            if (_disposed) return;
            _disposed = true;

            StopWatching();
            _cts.Cancel();
            _cts.Dispose();
        }
    }

    private sealed class DebounceState : IDisposable
    {
        private readonly object _lock = new();
        private Timer? _timer;
        private bool _disposed;

        public string FullPath { get; }
        public WatcherChangeTypes LastChangeType { get; private set; }
        public string? OldFullPath { get; private set; }
        public TaskCompletionSource<bool>? CompletionSource { get; private set; }

        public DebounceState(
            string fullPath,
            WatcherChangeTypes changeType,
            string? oldFullPath,
            int debounceMs,
            TimerCallback callback,
            TaskCompletionSource<bool>? completionSource)
        {
            FullPath = fullPath;
            LastChangeType = changeType;
            OldFullPath = oldFullPath;
            CompletionSource = completionSource;
            _timer = new Timer(callback, this, debounceMs, Timeout.Infinite);
        }

        public void Reset(WatcherChangeTypes newChangeType, string? newOldFullPath, int debounceMs, TaskCompletionSource<bool>? completionSource)
        {
            lock (_lock)
            {
                if (_disposed) return;

                _timer?.Change(debounceMs, Timeout.Infinite);

                if (newChangeType == WatcherChangeTypes.Deleted)
                {
                    LastChangeType = WatcherChangeTypes.Deleted;
                }
                else if (newChangeType == WatcherChangeTypes.Renamed)
                {
                    LastChangeType = WatcherChangeTypes.Renamed;
                    OldFullPath = newOldFullPath ?? OldFullPath;
                }
                else if (LastChangeType != WatcherChangeTypes.Deleted)
                {
                    LastChangeType = newChangeType;
                }

                if (completionSource != null)
                {
                    CompletionSource = completionSource;
                }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                _timer?.Dispose();
                _timer = null;
            }
        }
    }

    private sealed record FileAction(
        string FullPath,
        WatcherChangeTypes ChangeType,
        string? OldFullPath,
        TaskCompletionSource<bool>? CompletionSource);
}
