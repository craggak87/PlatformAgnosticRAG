namespace CellAssistant.UnitTests.Infrastructure.StressTests;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Application.DTOs;
using CellAssistant.Application.Options;
using CellAssistant.Application.Ports;
using CellAssistant.Domain.Entities;
using CellAssistant.Domain.Enums;
using CellAssistant.Infrastructure.Indexing;
using CellAssistant.Infrastructure.Parsing;
using CellAssistant.Infrastructure.Persistence.Sqlite;
using CellAssistant.Infrastructure.Watcher;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

public class Milestone3StressTests : IDisposable
{
    private readonly string _tempVault;
    private readonly SqliteConnectionFactory _sqliteFactory;
    private readonly SqliteDocumentRepository _sqliteDocRepo;
    private readonly SqliteIndexerStateRepository _sqliteStateRepo;
    private readonly IVectorStore _vectorStore = Substitute.For<IVectorStore>();
    private readonly IEmbeddingService _embeddingService = Substitute.For<IEmbeddingService>();
    private readonly List<IDocumentParser> _parsers = new();

    public Milestone3StressTests()
    {
        _tempVault = Path.Combine(Path.GetTempPath(), "m3_stress_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempVault);

        // Create standard vault folders
        foreach (var folder in new[] { "Equipment", "Faults", "PLC", "Robot", "Manuals", "Maintenance" })
        {
            Directory.CreateDirectory(Path.Combine(_tempVault, folder));
        }

        // Real SQLite in-memory database with schema initialization
        _sqliteFactory = new SqliteConnectionFactory(":memory:");
        var initializer = new SqliteDatabaseInitializer(_sqliteFactory);
        initializer.Initialize();
        _sqliteDocRepo = new SqliteDocumentRepository(_sqliteFactory);
        _sqliteStateRepo = new SqliteIndexerStateRepository(_sqliteFactory);

        // Parsers
        _parsers.Add(new MarkdigYamlParser());
        _parsers.Add(new PlainTextParser());

        // Default mock embedding
        _embeddingService.GenerateBatchEmbeddingsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var texts = ci.Arg<IReadOnlyList<string>>();
                return texts.Select(_ => new ReadOnlyMemory<float>(new float[768])).ToList();
            });
    }

    public void Dispose()
    {
        _sqliteFactory.Dispose();
        if (Directory.Exists(_tempVault))
        {
            try { Directory.Delete(_tempVault, recursive: true); } catch { }
        }
    }

    private DocumentIndexer CreateIndexer(string? rootPath = null)
    {
        var options = Options.Create(new KnowledgeBaseOptions
        {
            RootPath = rootPath ?? _tempVault
        });

        return new DocumentIndexer(
            _parsers,
            _sqliteDocRepo,
            _vectorStore,
            _embeddingService,
            _sqliteStateRepo,
            options,
            NullLogger<DocumentIndexer>.Instance);
    }

    // =========================================================================
    // 1. DocumentIndexer Filtering Stress Tests
    // =========================================================================

    [Fact]
    public async Task Filtering_ExcludesObsidianFolder_AtAnyDepth()
    {
        // Setup obsidian files at root and deeply nested
        var obsDir1 = Path.Combine(_tempVault, ".obsidian");
        var obsDir2 = Path.Combine(_tempVault, ".obsidian", "plugins", "dataview");
        var obsDir3 = Path.Combine(_tempVault, "Equipment", ".obsidian", "sub");
        Directory.CreateDirectory(obsDir1);
        Directory.CreateDirectory(obsDir2);
        Directory.CreateDirectory(obsDir3);

        var obsFile1 = Path.Combine(obsDir1, "workspace.json");
        var obsFile2 = Path.Combine(obsDir1, "notes.md");
        var obsFile3 = Path.Combine(obsDir2, "main.js");
        var obsFile4 = Path.Combine(obsDir3, "nested_note.md");

        await File.WriteAllTextAsync(obsFile1, "{\"active\": true}");
        await File.WriteAllTextAsync(obsFile2, "# Obsidian Internal Note");
        await File.WriteAllTextAsync(obsFile3, "console.log('dataview');");
        await File.WriteAllTextAsync(obsFile4, "# Nested Obsidian Note");

        // Add 1 valid file
        var validFile = Path.Combine(_tempVault, "Equipment", "valid.md");
        await File.WriteAllTextAsync(validFile, "# Valid Robot Note\nDetails here.");

        var indexer = CreateIndexer();
        var result = await indexer.IndexAllAsync();

        // Empirically verify only the 1 valid file was scanned and indexed
        result.TotalFilesScanned.Should().Be(1);
        result.FilesIndexed.Should().Be(1);
        result.FilesSkippedUnchanged.Should().Be(0);

        // Verify direct IndexFileAsync rejects all .obsidian files
        (await indexer.IndexFileAsync(obsFile1)).Should().BeFalse();
        (await indexer.IndexFileAsync(obsFile2)).Should().BeFalse();
        (await indexer.IndexFileAsync(obsFile4)).Should().BeFalse();

        // Verify SQLite only holds the 1 valid document
        var docs = await _sqliteDocRepo.GetAllAsync();
        docs.Should().HaveCount(1);
        docs[0].FileName.Should().Be("valid.md");
    }

    [Fact]
    public async Task Filtering_ExcludesHiddenDotfilesAndDirectories()
    {
        // Git repo directories & hooks
        var gitDir = Path.Combine(_tempVault, ".git", "hooks");
        Directory.CreateDirectory(gitDir);
        await File.WriteAllTextAsync(Path.Combine(gitDir, "pre-commit.md"), "# Git Hook");
        await File.WriteAllTextAsync(Path.Combine(_tempVault, ".git", "config"), "[core]");

        // Root dotfiles
        await File.WriteAllTextAsync(Path.Combine(_tempVault, ".DS_Store"), "binary mac os metadata");
        await File.WriteAllTextAsync(Path.Combine(_tempVault, ".hidden_doc.md"), "# Hidden doc");

        // Subdirectory dotfiles and hidden folders
        var hiddenSubDir = Path.Combine(_tempVault, "Equipment", ".internal");
        Directory.CreateDirectory(hiddenSubDir);
        await File.WriteAllTextAsync(Path.Combine(hiddenSubDir, "spec.md"), "# Secret Spec");
        await File.WriteAllTextAsync(Path.Combine(_tempVault, "Equipment", ".DS_Store"), "metadata");

        // 1 valid file
        var validFile = Path.Combine(_tempVault, "Equipment", "PLC01.md");
        await File.WriteAllTextAsync(validFile, "# PLC 01 Controller");

        var indexer = CreateIndexer();
        var result = await indexer.IndexAllAsync();

        result.TotalFilesScanned.Should().Be(1);
        result.FilesIndexed.Should().Be(1);

        (await indexer.IndexFileAsync(Path.Combine(_tempVault, ".hidden_doc.md"))).Should().BeFalse();
        (await indexer.IndexFileAsync(Path.Combine(hiddenSubDir, "spec.md"))).Should().BeFalse();
    }

    [Fact]
    public async Task Filtering_ExcludesTemporaryAndBackupFiles()
    {
        // Temporary editor files
        await File.WriteAllTextAsync(Path.Combine(_tempVault, "Equipment", "~test.md"), "Tilde prefix editor lock");
        await File.WriteAllTextAsync(Path.Combine(_tempVault, "Equipment", "draft.md~"), "Tilde suffix editor backup");
        await File.WriteAllTextAsync(Path.Combine(_tempVault, "Equipment", "file.tmp"), "Temporary file");
        await File.WriteAllTextAsync(Path.Combine(_tempVault, "Equipment", "backup.TMP"), "Case-insensitive TMP");
        await File.WriteAllTextAsync(Path.Combine(_tempVault, "Equipment", "~$WordLock.pdf"), "Word temporary lock");

        // 1 valid file
        var validFile = Path.Combine(_tempVault, "Equipment", "VFD01.md");
        await File.WriteAllTextAsync(validFile, "# VFD 01 Inverter");

        var indexer = CreateIndexer();
        var result = await indexer.IndexAllAsync();

        result.TotalFilesScanned.Should().Be(1);
        result.FilesIndexed.Should().Be(1);

        (await indexer.IndexFileAsync(Path.Combine(_tempVault, "Equipment", "~test.md"))).Should().BeFalse();
        (await indexer.IndexFileAsync(Path.Combine(_tempVault, "Equipment", "draft.md~"))).Should().BeFalse();
        (await indexer.IndexFileAsync(Path.Combine(_tempVault, "Equipment", "file.tmp"))).Should().BeFalse();
        (await indexer.IndexFileAsync(Path.Combine(_tempVault, "Equipment", "backup.TMP"))).Should().BeFalse();
    }

    [Theory]
    [InlineData("manual.exe")]
    [InlineData("data.json")]
    [InlineData("schema.yaml")]
    [InlineData("wiring.png")]
    [InlineData("program.py")]
    [InlineData("values.csv")]
    [InlineData("README")]
    public async Task Filtering_ExcludesUnsupportedExtensions(string fileName)
    {
        var filePath = Path.Combine(_tempVault, "Equipment", fileName);
        await File.WriteAllTextAsync(filePath, "Some content");

        var indexer = CreateIndexer();
        var result = await indexer.IndexAllAsync();

        result.TotalFilesScanned.Should().Be(0);
        result.FilesIndexed.Should().Be(0);

        (await indexer.IndexFileAsync(filePath)).Should().BeFalse();
    }

    // =========================================================================
    // 2. SHA-256 Hash Comparison Stress Tests
    // =========================================================================

    [Theory]
    [InlineData("", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("a", "ca978112ca1bbdcafac231b39a23dc4da786eff8147c4e72b9807785afee48bb")]
    [InlineData("abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq", "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1")]
    [InlineData("The quick brown fox jumps over the lazy dog", "d7a8fbb307d7809469ca9abcb0082e4f8d5651e46d3cdb762d02d0bf37c9e592")]
    public void Sha256_ExactNistHashVectors_MatchByteComputation(string input, string expectedHash)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = ChunkContextEnricher.ComputeSha256(bytes);

        hash.Should().Be(expectedHash);
        hash.Should().MatchRegex("^[a-f0-9]{64}$");
    }

    [Fact]
    public void Sha256_ExactNistHashVector_OneMillionACharacters()
    {
        // FIPS 180-2 standard test vector: 1,000,000 'a' characters
        var oneMillionA = new byte[1_000_000];
        Array.Fill(oneMillionA, (byte)'a');

        var hash = ChunkContextEnricher.ComputeSha256(oneMillionA);
        hash.Should().Be("cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0");
    }

    [Fact]
    public async Task Sha256_FileHashing_MatchesNistHashVectorAndStoresLowercaseInSqlite()
    {
        var testFile = Path.Combine(_tempVault, "Equipment", "nist_test.md");
        var content = "The quick brown fox jumps over the lazy dog";
        await File.WriteAllTextAsync(testFile, content);

        var indexer = CreateIndexer();
        var indexed = await indexer.IndexFileAsync(testFile);
        indexed.Should().BeTrue();

        var doc = await _sqliteDocRepo.GetByFilePathAsync(Path.GetFullPath(testFile));
        doc.Should().NotBeNull();
        doc!.ContentHash.Should().Be("d7a8fbb307d7809469ca9abcb0082e4f8d5651e46d3cdb762d02d0bf37c9e592");
        doc.ContentHash.Should().MatchRegex("^[a-f0-9]{64}$");
    }

    [Fact]
    public async Task Sha256_ZeroRecalculationWhenHashUnchanged()
    {
        var notePath = Path.Combine(_tempVault, "Equipment", "static_note.md");
        await File.WriteAllTextAsync(notePath, "# Static Note\nUnchanging content.");

        var indexer = CreateIndexer();

        // 1. Initial indexing run
        var run1 = await indexer.IndexAllAsync();
        run1.FilesIndexed.Should().Be(1);
        run1.FilesSkippedUnchanged.Should().Be(0);

        _embeddingService.ClearReceivedCalls();
        _vectorStore.ClearReceivedCalls();

        // 2. Second indexing run on unchanged file
        var run2 = await indexer.IndexAllAsync();
        run2.TotalFilesScanned.Should().Be(1);
        run2.FilesIndexed.Should().Be(0);
        run2.FilesSkippedUnchanged.Should().Be(1);

        // Zero recalculation: embedding and vector store must NOT have been called!
        await _embeddingService.DidNotReceiveWithAnyArgs()
            .GenerateBatchEmbeddingsAsync(default!, default);
        await _vectorStore.DidNotReceiveWithAnyArgs()
            .UpsertVectorsAsync(default!, default);

        // 3. Direct single file index on unchanged file
        var singleResult = await indexer.IndexFileAsync(notePath);
        singleResult.Should().BeFalse();

        await _embeddingService.DidNotReceiveWithAnyArgs()
            .GenerateBatchEmbeddingsAsync(default!, default);
        await _vectorStore.DidNotReceiveWithAnyArgs()
            .UpsertVectorsAsync(default!, default);
    }

    [Fact]
    public async Task Sha256_CaseSensitivity_HandlesMixedCaseInSqliteGracefully()
    {
        var notePath = Path.Combine(_tempVault, "Equipment", "case_test.md");
        var bytes = Encoding.UTF8.GetBytes("Case test note content");
        await File.WriteAllBytesAsync(notePath, bytes);
        var lowerHash = ChunkContextEnricher.ComputeSha256(bytes);
        var upperHash = lowerHash.ToUpperInvariant();

        // Seed SQLite with uppercase hash
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            FileName = "case_test.md",
            FilePath = Path.GetFullPath(notePath),
            RelativePath = "Equipment/case_test.md",
            DocumentType = DocumentType.Markdown,
            Folder = "Equipment",
            ContentHash = upperHash,
            CreatedUtc = DateTimeOffset.UtcNow,
            ModifiedUtc = DateTimeOffset.UtcNow,
            IndexedUtc = DateTimeOffset.UtcNow
        };
        await _sqliteDocRepo.UpsertAsync(doc);

        var indexer = CreateIndexer();

        // IndexAllAsync should recognize the hash matches case-insensitively and skip reindexing
        var runResult = await indexer.IndexAllAsync();
        runResult.FilesSkippedUnchanged.Should().Be(1);
        runResult.FilesIndexed.Should().Be(0);

        // IndexFileAsync should also return false (unchanged)
        var fileResult = await indexer.IndexFileAsync(notePath);
        fileResult.Should().BeFalse();
    }

    // =========================================================================
    // 3. Deleted File Cleanup Stress Tests
    // =========================================================================

    [Fact]
    public async Task DeletedFileCleanup_IndexAllAsync_PurgesVectorsInQdrantAndRecordsInSqlite()
    {
        // 1. Create and index 3 files
        var file1 = Path.Combine(_tempVault, "Equipment", "doc1.md");
        var file2 = Path.Combine(_tempVault, "Faults", "doc2.md");
        var file3 = Path.Combine(_tempVault, "Manuals", "doc3.txt");

        await File.WriteAllTextAsync(file1, "# Doc 1 Robot");
        await File.WriteAllTextAsync(file2, "# Doc 2 Fault FLT-001");
        await File.WriteAllTextAsync(file3, "Doc 3 Manual instructions");

        var indexer = CreateIndexer();
        var run1 = await indexer.IndexAllAsync();
        run1.FilesIndexed.Should().Be(3);

        var docsInDb = await _sqliteDocRepo.GetAllAsync();
        docsInDb.Should().HaveCount(3);

        var doc1 = docsInDb.First(d => d.FileName == "doc1.md");
        var doc2 = docsInDb.First(d => d.FileName == "doc2.md");
        var doc3 = docsInDb.First(d => d.FileName == "doc3.txt");

        _vectorStore.ClearReceivedCalls();

        // 2. Physically delete doc1.md from disk
        File.Delete(file1);
        File.Exists(file1).Should().BeFalse();

        // 3. Re-run IndexAllAsync
        var run2 = await indexer.IndexAllAsync();
        run2.TotalFilesScanned.Should().Be(2);
        run2.FilesIndexed.Should().Be(0);
        run2.FilesSkippedUnchanged.Should().Be(2);
        run2.FilesDeleted.Should().Be(1);
        run2.Success.Should().BeTrue();

        // Verify vectors in Qdrant were deleted for doc1 ID
        await _vectorStore.Received(1).DeleteByDocumentIdAsync(doc1.Id, Arg.Any<CancellationToken>());
        await _vectorStore.DidNotReceive().DeleteByDocumentIdAsync(doc2.Id, Arg.Any<CancellationToken>());
        await _vectorStore.DidNotReceive().DeleteByDocumentIdAsync(doc3.Id, Arg.Any<CancellationToken>());

        // Verify record in SQLite was deleted for doc1 ID
        var remainingDocs = await _sqliteDocRepo.GetAllAsync();
        remainingDocs.Should().HaveCount(2);
        remainingDocs.Select(d => d.Id).Should().NotContain(doc1.Id);
        remainingDocs.Select(d => d.Id).Should().Contain(new[] { doc2.Id, doc3.Id });
    }

    [Fact]
    public async Task DeletedFileCleanup_RemoveFileAsync_PurgesVectorsAndSqliteRecord()
    {
        var file = Path.Combine(_tempVault, "Faults", "FLT-002.md");
        await File.WriteAllTextAsync(file, "# Gripper fault FLT-002");

        var indexer = CreateIndexer();
        await indexer.IndexFileAsync(file);

        var doc = await _sqliteDocRepo.GetByFilePathAsync(Path.GetFullPath(file));
        doc.Should().NotBeNull();
        var docId = doc!.Id;

        _vectorStore.ClearReceivedCalls();

        // Remove via indexer
        var removed = await indexer.RemoveFileAsync(file);
        removed.Should().BeTrue();

        // Verify Qdrant purge
        await _vectorStore.Received(1).DeleteByDocumentIdAsync(docId, Arg.Any<CancellationToken>());

        // Verify SQLite deletion
        var afterDoc = await _sqliteDocRepo.GetByIdAsync(docId);
        afterDoc.Should().BeNull();

        // Idempotent secondary call returns false and does not call Qdrant again
        var secondRemove = await indexer.RemoveFileAsync(file);
        secondRemove.Should().BeFalse();
        await _vectorStore.Received(1).DeleteByDocumentIdAsync(docId, Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // 4. DebouncedFileWatcherService Stress Tests
    // =========================================================================

    [Fact]
    public async Task Debounce_RapidWriteBurst_TenWritesIn100ms_TriggersExactlyOneIndexCall()
    {
        var mockIndexer = Substitute.For<IDocumentIndexer>();
        var options = Options.Create(new KnowledgeBaseOptions
        {
            RootPath = _tempVault,
            WatcherDebounceMs = 250 // 250ms sliding window
        });

        using var watcher = new DebouncedFileWatcherService(options, mockIndexer, NullLogger<DebouncedFileWatcherService>.Instance);
        await watcher.StartAsync();

        string file = Path.Combine(_tempVault, "Equipment", "rapid_burst.md");
        await File.WriteAllTextAsync(file, "# Initial content");

        // Simulate 10 rapid write events within 100ms (10ms apart)
        var tcsList = new List<TaskCompletionSource<bool>>();
        for (int i = 0; i < 10; i++)
        {
            var tcs = new TaskCompletionSource<bool>();
            tcsList.Add(tcs);
            watcher.ProcessRawEvent(WatcherChangeTypes.Changed, file, tcs: tcs);
            await Task.Delay(10);
        }

        // Wait for the debounced event to process
        var finalTcs = tcsList.Last();
        var completed = await Task.WhenAny(finalTcs.Task, Task.Delay(3000));
        completed.Should().Be(finalTcs.Task, "The debounced action should complete within 3 seconds");
        (await finalTcs.Task).Should().BeTrue();

        // Give any stray dispatches a moment to reveal duplicate calls
        await Task.Delay(300);

        // Empirically verify that IndexFileAsync was called EXACTLY ONCE
        await mockIndexer.Received(1).IndexFileAsync(file, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Debounce_RapidBurstsAcrossMultipleFiles_DebouncesPerPathIndependently()
    {
        var mockIndexer = Substitute.For<IDocumentIndexer>();
        var options = Options.Create(new KnowledgeBaseOptions
        {
            RootPath = _tempVault,
            WatcherDebounceMs = 200
        });

        using var watcher = new DebouncedFileWatcherService(options, mockIndexer, NullLogger<DebouncedFileWatcherService>.Instance);
        await watcher.StartAsync();

        string fileA = Path.Combine(_tempVault, "Equipment", "fileA.md");
        string fileB = Path.Combine(_tempVault, "Faults", "fileB.md");
        await File.WriteAllTextAsync(fileA, "# File A");
        await File.WriteAllTextAsync(fileB, "# File B");

        var tcsA = new List<TaskCompletionSource<bool>>();
        var tcsB = new List<TaskCompletionSource<bool>>();

        // Interleaved bursts: 10 writes on fileA and 10 writes on fileB in 100ms
        for (int i = 0; i < 10; i++)
        {
            var a = new TaskCompletionSource<bool>();
            var b = new TaskCompletionSource<bool>();
            tcsA.Add(a);
            tcsB.Add(b);

            watcher.ProcessRawEvent(WatcherChangeTypes.Changed, fileA, tcs: a);
            watcher.ProcessRawEvent(WatcherChangeTypes.Changed, fileB, tcs: b);
            await Task.Delay(10);
        }

        await Task.WhenAll(tcsA.Last().Task, tcsB.Last().Task).WaitAsync(TimeSpan.FromSeconds(3));

        await Task.Delay(250);

        // Each file must be indexed exactly once
        await mockIndexer.Received(1).IndexFileAsync(fileA, Arg.Any<CancellationToken>());
        await mockIndexer.Received(1).IndexFileAsync(fileB, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LockedFile_ExponentialBackoffRetry_RecoversWhenFileReleasedDuringRetry()
    {
        string lockedFile = Path.Combine(_tempVault, "Equipment", "locked_temp.md");
        await File.WriteAllTextAsync(lockedFile, "Initial content");

        var lockAcquired = new TaskCompletionSource<bool>();
        var lockRelease = new TaskCompletionSource<bool>();

        // Hold an exclusive lock with FileShare.None, release after lockRelease signals
        var lockTask = Task.Run(async () =>
        {
            using var fs = new FileStream(lockedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            lockAcquired.SetResult(true);
            await lockRelease.Task;
        });

        // Ensure lock is actually held before testing retry
        await lockAcquired.Task;

        // Release the lock after 120ms (falls in attempt 2: 50ms + 100ms = 150ms)
        _ = Task.Run(async () =>
        {
            await Task.Delay(120);
            lockRelease.TrySetResult(true);
        });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool available = await DebouncedFileWatcherService.WaitForFileAvailableAsync(
            lockedFile,
            maxRetries: 5,
            initialDelayMs: 50,
            logger: NullLogger<DebouncedFileWatcherService>.Instance);
        sw.Stop();

        await lockTask;

        available.Should().BeTrue();
        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(50);
    }

    [Fact]
    public async Task LockedFile_PermanentLock_ExhaustsRetriesAndFailsGracefullyWithoutThrowing()
    {
        string lockedFile = Path.Combine(_tempVault, "Equipment", "locked_perm.md");
        await File.WriteAllTextAsync(lockedFile, "Permanent lock content");

        using var fs = new FileStream(lockedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        // 3 retries, initial delay 20ms: attempt 1 (20ms), attempt 2 (40ms) -> total 60ms
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool available = await DebouncedFileWatcherService.WaitForFileAvailableAsync(
            lockedFile,
            maxRetries: 3,
            initialDelayMs: 20,
            logger: NullLogger<DebouncedFileWatcherService>.Instance);
        sw.Stop();

        available.Should().BeFalse();
        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(40);
    }

    [Fact]
    public async Task LockedFile_WatcherIntegration_RecoversAndIndexesWhenUnlocked()
    {
        var mockIndexer = Substitute.For<IDocumentIndexer>();
        mockIndexer.IndexFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var options = Options.Create(new KnowledgeBaseOptions
        {
            RootPath = _tempVault,
            WatcherDebounceMs = 50
        });

        using var watcher = new DebouncedFileWatcherService(options, mockIndexer, NullLogger<DebouncedFileWatcherService>.Instance);
        await watcher.StartAsync();

        string file = Path.Combine(_tempVault, "Equipment", "locked_service.md");
        await File.WriteAllTextAsync(file, "# Locked Service Note");

        var lockAcquired = new TaskCompletionSource<bool>();
        var lockRelease = new TaskCompletionSource<bool>();

        // Hold lock until signaled
        var lockHolder = Task.Run(async () =>
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            lockAcquired.SetResult(true);
            await lockRelease.Task;
        });

        await lockAcquired.Task;

        // Release after 80ms
        _ = Task.Run(async () =>
        {
            await Task.Delay(80);
            lockRelease.TrySetResult(true);
        });

        var tcs = new TaskCompletionSource<bool>();
        watcher.ProcessRawEvent(WatcherChangeTypes.Changed, file, tcs: tcs);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(3000));
        completed.Should().Be(tcs.Task);
        (await tcs.Task).Should().BeTrue();

        await lockHolder;

        await mockIndexer.Received(1).IndexFileAsync(file, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LockedFile_PermanentLock_DoesNotBlockSubsequentEventsOnOtherFiles()
    {
        var mockIndexer = Substitute.For<IDocumentIndexer>();
        mockIndexer.IndexFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var options = Options.Create(new KnowledgeBaseOptions
        {
            RootPath = _tempVault,
            WatcherDebounceMs = 50
        });

        using var watcher = new DebouncedFileWatcherService(options, mockIndexer, NullLogger<DebouncedFileWatcherService>.Instance);
        await watcher.StartAsync();

        string lockedFile = Path.Combine(_tempVault, "Equipment", "permanently_locked.md");
        string normalFile = Path.Combine(_tempVault, "Equipment", "normal_subsequent.md");

        await File.WriteAllTextAsync(lockedFile, "# Permanently locked");
        await File.WriteAllTextAsync(normalFile, "# Normal file");

        // Hold permanent lock on lockedFile
        using var fs = new FileStream(lockedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var tcsLocked = new TaskCompletionSource<bool>();
        watcher.ProcessRawEvent(WatcherChangeTypes.Changed, lockedFile, tcs: tcsLocked);

        // Fire normal file immediately after
        var tcsNormal = new TaskCompletionSource<bool>();
        watcher.ProcessRawEvent(WatcherChangeTypes.Changed, normalFile, tcs: tcsNormal);

        // Normal file must complete within reasonable time despite the locked file
        var completed = await Task.WhenAny(tcsNormal.Task, Task.Delay(5000));
        completed.Should().Be(tcsNormal.Task, "Subsequent normal file should be processed even if prior file was locked");
        (await tcsNormal.Task).Should().BeTrue();

        // Normal file was indexed
        await mockIndexer.Received(1).IndexFileAsync(normalFile, Arg.Any<CancellationToken>());

        // Permanently locked file was skipped (never reached IndexFileAsync)
        await mockIndexer.DidNotReceive().IndexFileAsync(lockedFile, Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // 5. Volume, Concurrency & Edge Cases
    // =========================================================================

    [Fact]
    public async Task Volume_FiftyFilesBatchReconciliation_CalculatesCorrectMetrics()
    {
        var subfolders = new[] { "Equipment", "Faults", "PLC", "Robot", "Manuals" };
        var createdFiles = new List<string>();

        for (int i = 0; i < 50; i++)
        {
            var folder = subfolders[i % subfolders.Length];
            var filePath = Path.Combine(_tempVault, folder, $"note_{i:D3}.md");
            await File.WriteAllTextAsync(filePath, $"# Note {i}\nContent for document {i} with identifier ROBOT{i:D2}.");
            createdFiles.Add(filePath);
        }

        var indexer = CreateIndexer();

        // 1. First run: indexes all 50
        var run1 = await indexer.IndexAllAsync();
        run1.TotalFilesScanned.Should().Be(50);
        run1.FilesIndexed.Should().Be(50);
        run1.FilesSkippedUnchanged.Should().Be(0);
        run1.FilesDeleted.Should().Be(0);
        run1.ErrorsCount.Should().Be(0);
        run1.Success.Should().BeTrue();

        // 2. Modify 10 files, delete 10 files, leave 30 unchanged
        for (int i = 0; i < 10; i++)
        {
            await File.WriteAllTextAsync(createdFiles[i], $"# Modified Note {i}\nUpdated content.");
        }
        for (int i = 10; i < 20; i++)
        {
            File.Delete(createdFiles[i]);
        }

        // 3. Second run: verifies delta reconciliation
        var run2 = await indexer.IndexAllAsync();
        run2.TotalFilesScanned.Should().Be(40);
        run2.FilesIndexed.Should().Be(10);
        run2.FilesSkippedUnchanged.Should().Be(30);
        run2.FilesDeleted.Should().Be(10);
        run2.ErrorsCount.Should().Be(0);
        run2.Success.Should().BeTrue();

        // Verify SQLite state
        var remainingDocs = await _sqliteDocRepo.GetAllAsync();
        remainingDocs.Should().HaveCount(40);
    }

    [Fact]
    public async Task EmptyFile_ZeroBytes_IndexedCleanlyWithZeroChunksAndStandardHash()
    {
        var emptyMd = Path.Combine(_tempVault, "Equipment", "empty.md");
        var emptyTxt = Path.Combine(_tempVault, "Manuals", "empty.txt");
        await File.WriteAllBytesAsync(emptyMd, Array.Empty<byte>());
        await File.WriteAllBytesAsync(emptyTxt, Array.Empty<byte>());

        var indexer = CreateIndexer();
        var result = await indexer.IndexAllAsync();

        result.FilesIndexed.Should().Be(2);
        result.ErrorsCount.Should().Be(0);

        var docMd = await _sqliteDocRepo.GetByFilePathAsync(Path.GetFullPath(emptyMd));
        docMd.Should().NotBeNull();
        docMd!.ChunkCount.Should().Be(0);
        docMd.ContentHash.Should().Be("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");

        // Verify Qdrant was NOT called with empty vectors
        await _vectorStore.DidNotReceive().UpsertVectorsAsync(
            Arg.Is<IReadOnlyList<VectorPoint>>(pts => pts.Count > 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Concurrency_ParallelCalls_SerializedSafelyViaSemaphore()
    {
        var file = Path.Combine(_tempVault, "Equipment", "concurrent.md");
        await File.WriteAllTextAsync(file, "# Concurrent Note");

        var indexer = CreateIndexer();

        // Launch 10 simultaneous calls to IndexAllAsync and IndexFileAsync
        var tasks = new List<Task>();
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(Task.Run(async () => await indexer.IndexAllAsync()));
            tasks.Add(Task.Run(async () => await indexer.IndexFileAsync(file)));
        }

        var act = async () => await Task.WhenAll(tasks);
        await act.Should().NotThrowAsync();

        var state = await _sqliteStateRepo.GetStateAsync();
        state.Status.Should().Be(IndexerStatus.Idle);
    }
}
