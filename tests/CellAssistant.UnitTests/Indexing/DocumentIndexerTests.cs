namespace CellAssistant.UnitTests.Indexing;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Application.DTOs;
using CellAssistant.Application.Options;
using CellAssistant.Application.Ports;
using CellAssistant.Domain.Entities;
using CellAssistant.Domain.Enums;
using CellAssistant.Infrastructure.Indexing;
using CellAssistant.Infrastructure.Parsing;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

public class DocumentIndexerTests : IDisposable
{
    private readonly string _tempVault;
    private readonly IDocumentRepository _docRepo = Substitute.For<IDocumentRepository>();
    private readonly IVectorStore _vectorStore = Substitute.For<IVectorStore>();
    private readonly IEmbeddingService _embeddingService = Substitute.For<IEmbeddingService>();
    private readonly IIndexerStateRepository _stateRepo = Substitute.For<IIndexerStateRepository>();
    private readonly List<IDocumentParser> _parsers = new();

    public DocumentIndexerTests()
    {
        _tempVault = Path.Combine(Path.GetTempPath(), "indexer_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempVault);
        Directory.CreateDirectory(Path.Combine(_tempVault, "Equipment"));
        Directory.CreateDirectory(Path.Combine(_tempVault, "Faults"));

        _parsers.Add(new MarkdigYamlParser());
        _parsers.Add(new PlainTextParser());

        // Default mock embedding returns 768-dim float array
        _embeddingService.GenerateBatchEmbeddingsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var texts = callInfo.Arg<IReadOnlyList<string>>();
                return texts.Select(_ => new ReadOnlyMemory<float>(new float[768])).ToList();
            });
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempVault))
        {
            try { Directory.Delete(_tempVault, true); } catch { }
        }
    }

    private DocumentIndexer CreateIndexer(string? vaultPath = null)
    {
        var options = Options.Create(new KnowledgeBaseOptions
        {
            RootPath = vaultPath ?? _tempVault
        });

        return new DocumentIndexer(
            _parsers,
            _docRepo,
            _vectorStore,
            _embeddingService,
            _stateRepo,
            options,
            NullLogger<DocumentIndexer>.Instance);
    }

    [Fact]
    public async Task IndexAllAsync_WhenRootDirectoryDoesNotExist_ReturnsEmptyResultAndSetsIdleState()
    {
        var missingDir = Path.Combine(_tempVault, "non_existent_vault");
        var indexer = CreateIndexer(missingDir);

        var result = await indexer.IndexAllAsync();

        result.Success.Should().BeTrue();
        result.TotalFilesScanned.Should().Be(0);
        result.FilesIndexed.Should().Be(0);

        await _stateRepo.Received().UpdateStateAsync(
            Arg.Is<IndexerState>(s => s.Status == IndexerStatus.Idle),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IndexAllAsync_WhenNewFilesDiscovered_ParsesEmbedsAndUpsertsToQdrantAndSqlite()
    {
        var notePath = Path.Combine(_tempVault, "Equipment", "robot.md");
        await File.WriteAllTextAsync(notePath, "# Robot 1\nDetails here.");

        _docRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<Document>());

        var indexer = CreateIndexer();
        var result = await indexer.IndexAllAsync();

        result.Success.Should().BeTrue();
        result.TotalFilesScanned.Should().Be(1);
        result.FilesIndexed.Should().Be(1);
        result.FilesSkippedUnchanged.Should().Be(0);

        await _vectorStore.Received(1).UpsertVectorsAsync(
            Arg.Is<IReadOnlyList<VectorPoint>>(pts => pts.Count == 1),
            Arg.Any<CancellationToken>());

        await _docRepo.Received(1).UpsertAsync(
            Arg.Is<Document>(d => d.FileName == "robot.md" && d.Folder == "Equipment"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IndexAllAsync_WhenFileHashUnchanged_SkipsParsingAndEmbedding()
    {
        var notePath = Path.Combine(_tempVault, "Equipment", "robot.md");
        var rawBytes = System.Text.Encoding.UTF8.GetBytes("# Robot 1\nDetails here.");
        await File.WriteAllBytesAsync(notePath, rawBytes);
        var expectedHash = ChunkContextEnricher.ComputeSha256(rawBytes);

        var existingDoc = new Document
        {
            Id = Guid.NewGuid(),
            FilePath = notePath,
            FileName = "robot.md",
            ContentHash = expectedHash
        };

        _docRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<Document> { existingDoc });

        var indexer = CreateIndexer();
        var result = await indexer.IndexAllAsync();

        result.Success.Should().BeTrue();
        result.TotalFilesScanned.Should().Be(1);
        result.FilesIndexed.Should().Be(0);
        result.FilesSkippedUnchanged.Should().Be(1);

        await _vectorStore.DidNotReceive().UpsertVectorsAsync(
            Arg.Any<IReadOnlyList<VectorPoint>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IndexAllAsync_WhenFileHashChanged_DeletesPreviousVectorsAndReindexes()
    {
        var notePath = Path.Combine(_tempVault, "Equipment", "robot.md");
        await File.WriteAllTextAsync(notePath, "# Revision 2");

        var docId = Guid.NewGuid();
        var existingDoc = new Document
        {
            Id = docId,
            FilePath = notePath,
            FileName = "robot.md",
            ContentHash = "old_outdated_hash"
        };

        _docRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<Document> { existingDoc });

        var indexer = CreateIndexer();
        var result = await indexer.IndexAllAsync();

        result.Success.Should().BeTrue();
        result.FilesIndexed.Should().Be(1);

        // Deletes previous vectors by document ID
        await _vectorStore.Received(1).DeleteByDocumentIdAsync(docId, Arg.Any<CancellationToken>());

        // Upserts new vectors
        await _vectorStore.Received(1).UpsertVectorsAsync(
            Arg.Is<IReadOnlyList<VectorPoint>>(pts => pts.All(p => p.DocumentId == docId)),
            Arg.Any<CancellationToken>());

        // Updates metadata in SQLite
        await _docRepo.Received(1).UpsertAsync(
            Arg.Is<Document>(d => d.Id == docId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IndexAllAsync_WhenFileInSqliteMissingOnDisk_DeletesFromQdrantAndSqlite()
    {
        var missingFilePath = Path.Combine(_tempVault, "Faults", "deleted_fault.md");
        var docId = Guid.NewGuid();
        var existingDoc = new Document
        {
            Id = docId,
            FilePath = missingFilePath,
            FileName = "deleted_fault.md",
            ContentHash = "hash123"
        };

        _docRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<Document> { existingDoc });

        var indexer = CreateIndexer();
        var result = await indexer.IndexAllAsync();

        result.Success.Should().BeTrue();
        result.FilesDeleted.Should().Be(1);

        await _vectorStore.Received(1).DeleteByDocumentIdAsync(docId, Arg.Any<CancellationToken>());
        await _docRepo.Received(1).DeleteByIdAsync(docId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IndexAllAsync_IgnoresObsidianFolderAndHiddenDotFiles()
    {
        var obsidianDir = Path.Combine(_tempVault, ".obsidian");
        Directory.CreateDirectory(obsidianDir);
        await File.WriteAllTextAsync(Path.Combine(obsidianDir, "workspace.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(obsidianDir, "note.md"), "Ignored note");

        await File.WriteAllTextAsync(Path.Combine(_tempVault, ".hidden_note.md"), "Hidden note");
        await File.WriteAllTextAsync(Path.Combine(_tempVault, "Equipment", "~draft.md"), "Draft note");

        _docRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<Document>());

        var indexer = CreateIndexer();
        var result = await indexer.IndexAllAsync();

        result.TotalFilesScanned.Should().Be(0);
        result.FilesIndexed.Should().Be(0);
    }

    [Fact]
    public async Task IndexFileAsync_WhenFileHashUnchanged_ReturnsFalse()
    {
        var notePath = Path.Combine(_tempVault, "Equipment", "same.md");
        var rawBytes = System.Text.Encoding.UTF8.GetBytes("# Same content");
        await File.WriteAllBytesAsync(notePath, rawBytes);
        var expectedHash = ChunkContextEnricher.ComputeSha256(rawBytes);

        var existingDoc = new Document
        {
            Id = Guid.NewGuid(),
            FilePath = notePath,
            FileName = "same.md",
            ContentHash = expectedHash
        };

        _docRepo.GetByFilePathAsync(notePath, Arg.Any<CancellationToken>()).Returns(existingDoc);

        var indexer = CreateIndexer();
        var indexed = await indexer.IndexFileAsync(notePath);

        indexed.Should().BeFalse();
        await _vectorStore.DidNotReceive().UpsertVectorsAsync(Arg.Any<IReadOnlyList<VectorPoint>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IndexFileAsync_WhenFileIsNew_IndexesAndReturnsTrue()
    {
        var notePath = Path.Combine(_tempVault, "Equipment", "new_file.md");
        await File.WriteAllTextAsync(notePath, "# New file content");

        _docRepo.GetByFilePathAsync(notePath, Arg.Any<CancellationToken>()).Returns((Document?)null);

        var indexer = CreateIndexer();
        var indexed = await indexer.IndexFileAsync(notePath);

        indexed.Should().BeTrue();
        await _vectorStore.Received(1).UpsertVectorsAsync(Arg.Any<IReadOnlyList<VectorPoint>>(), Arg.Any<CancellationToken>());
        await _docRepo.Received(1).UpsertAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IndexFileAsync_WhenFileDoesNotExist_ReturnsFalse()
    {
        var indexer = CreateIndexer();
        var indexed = await indexer.IndexFileAsync(Path.Combine(_tempVault, "does_not_exist.md"));

        indexed.Should().BeFalse();
    }

    [Fact]
    public async Task IndexFileAsync_WhenFileIsIgnoredOrUnsupported_ReturnsFalse()
    {
        var unsupportedFile = Path.Combine(_tempVault, "Equipment", "program.exe");
        await File.WriteAllTextAsync(unsupportedFile, "executable binary");

        var indexer = CreateIndexer();
        var indexed = await indexer.IndexFileAsync(unsupportedFile);

        indexed.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveFileAsync_WhenFileExistsInSqlite_DeletesVectorsAndRecordReturnsTrue()
    {
        var filePath = Path.Combine(_tempVault, "Equipment", "to_delete.md");
        var docId = Guid.NewGuid();
        var existingDoc = new Document { Id = docId, FilePath = filePath };

        _docRepo.GetByFilePathAsync(filePath, Arg.Any<CancellationToken>()).Returns(existingDoc);

        var indexer = CreateIndexer();
        var removed = await indexer.RemoveFileAsync(filePath);

        removed.Should().BeTrue();
        await _vectorStore.Received(1).DeleteByDocumentIdAsync(docId, Arg.Any<CancellationToken>());
        await _docRepo.Received(1).DeleteByIdAsync(docId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveFileAsync_WhenFileDoesNotExistInSqlite_ReturnsFalse()
    {
        var filePath = Path.Combine(_tempVault, "Equipment", "unknown.md");
        _docRepo.GetByFilePathAsync(filePath, Arg.Any<CancellationToken>()).Returns((Document?)null);

        var indexer = CreateIndexer();
        var removed = await indexer.RemoveFileAsync(filePath);

        removed.Should().BeFalse();
        await _vectorStore.DidNotReceive().DeleteByDocumentIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IndexAllAsync_UpdatesIndexerStateToIndexingThenIdleOnSuccess()
    {
        _docRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<Document>());

        var indexer = CreateIndexer();
        var result = await indexer.IndexAllAsync();

        result.Success.Should().BeTrue();

        await _stateRepo.Received(1).UpdateStatusAsync(IndexerStatus.Indexing, null, Arg.Any<CancellationToken>());
        await _stateRepo.Received(1).UpdateStateAsync(
            Arg.Is<IndexerState>(s => s.Status == IndexerStatus.Idle && s.LastError == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IndexAllAsync_WhenCatastrophicErrorOccurs_SetsStateToFailedAndReturnsFailureResult()
    {
        _docRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("SQLite disk I/O failure"));

        var indexer = CreateIndexer();
        var result = await indexer.IndexAllAsync();

        result.Success.Should().BeFalse();
        result.ErrorsCount.Should().BeGreaterThan(0);

        await _stateRepo.Received().UpdateStateAsync(
            Arg.Is<IndexerState>(s => s.Status == IndexerStatus.Failed),
            Arg.Any<CancellationToken>());
    }
}
