namespace CellAssistant.IntegrationTests.Repositories;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CellAssistant.Domain.Entities;
using CellAssistant.Domain.Enums;
using CellAssistant.Domain.ValueObjects;
using CellAssistant.Infrastructure.Persistence.Sqlite;
using FluentAssertions;
using Xunit;

public class SqliteRepositoryIntegrationTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SqliteDatabaseInitializer _initializer;
    private readonly SqliteDocumentRepository _documentRepository;
    private readonly SqliteSearchHistoryRepository _historyRepository;
    private readonly SqliteIndexerStateRepository _indexerStateRepository;

    public SqliteRepositoryIntegrationTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "CellAssistant_Integration_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _dbPath = Path.Combine(_tempDirectory, "metadata.db");

        _connectionFactory = new SqliteConnectionFactory(_dbPath);
        _initializer = new SqliteDatabaseInitializer(_connectionFactory);
        _documentRepository = new SqliteDocumentRepository(_connectionFactory);
        _historyRepository = new SqliteSearchHistoryRepository(_connectionFactory);
        _indexerStateRepository = new SqliteIndexerStateRepository(_connectionFactory);
    }

    public void Dispose()
    {
        _connectionFactory.Dispose();

        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup for locked sqlite temp files
        }
    }

    [Fact]
    public async Task InitializeAsync_OnRealFile_CreatesTablesAndEnforcesWalMode()
    {
        // Act
        await _initializer.InitializeAsync();

        // Assert: Database file exists
        File.Exists(_dbPath).Should().BeTrue();

        using var conn = await _connectionFactory.CreateOpenConnectionAsync();

        // Assert: WAL mode pragma
        using (var pragmaCmd = conn.CreateCommand())
        {
            pragmaCmd.CommandText = "PRAGMA journal_mode;";
            var mode = await pragmaCmd.ExecuteScalarAsync();
            mode?.ToString()?.ToLowerInvariant().Should().Be("wal");
        }

        // Assert: Required tables exist
        using (var tableCmd = conn.CreateCommand())
        {
            tableCmd.CommandText = @"
                SELECT COUNT(*) FROM sqlite_master 
                WHERE type = 'table' 
                AND name IN ('documents', 'search_history', 'indexer_state');";
            var count = Convert.ToInt32(await tableCmd.ExecuteScalarAsync());
            count.Should().Be(3);
        }
    }

    [Fact]
    public async Task SqliteDocumentRepository_CrudLifecycle_PersistsAndRetrievesAccurately()
    {
        // Arrange
        await _initializer.InitializeAsync();

        var docId = Guid.NewGuid();
        var doc = new Document
        {
            Id = docId,
            FileName = "Robot Gripper.md",
            FilePath = "/opt/vault/Faults/Robot Gripper.md",
            RelativePath = "Faults/Robot Gripper.md",
            DocumentType = DocumentType.Markdown,
            Folder = "Faults",
            ContentHash = "sha256_mock_hash_12345",
            EquipmentId = "ROBOT01",
            Manufacturer = "ABB",
            Criticality = "High",
            Tags = new List<string> { "robotics", "pneumatics", "troubleshooting" },
            ChunkCount = 4,
            CreatedUtc = DateTimeOffset.UtcNow.AddDays(-1),
            ModifiedUtc = DateTimeOffset.UtcNow.AddHours(-2),
            IndexedUtc = DateTimeOffset.UtcNow
        };

        // Act 1: Initial Count
        int initialCount = await _documentRepository.GetCountAsync();
        initialCount.Should().Be(0);

        // Act 2: Upsert (Insert)
        await _documentRepository.UpsertAsync(doc);

        // Assert 2: Count updated
        int countAfterInsert = await _documentRepository.GetCountAsync();
        countAfterInsert.Should().Be(1);

        // Act 3: GetByIdAsync
        var retrievedById = await _documentRepository.GetByIdAsync(docId);
        retrievedById.Should().NotBeNull();
        retrievedById!.Id.Should().Be(docId);
        retrievedById.FileName.Should().Be("Robot Gripper.md");
        retrievedById.FilePath.Should().Be("/opt/vault/Faults/Robot Gripper.md");
        retrievedById.RelativePath.Should().Be("Faults/Robot Gripper.md");
        retrievedById.DocumentType.Should().Be(DocumentType.Markdown);
        retrievedById.Folder.Should().Be("Faults");
        retrievedById.EquipmentId.Should().Be("ROBOT01");
        retrievedById.Manufacturer.Should().Be("ABB");
        retrievedById.Criticality.Should().Be("High");
        retrievedById.Tags.Should().BeEquivalentTo(new[] { "robotics", "pneumatics", "troubleshooting" });
        retrievedById.ChunkCount.Should().Be(4);

        // Act 4: GetByFilePathAsync
        var retrievedByPath = await _documentRepository.GetByFilePathAsync("/opt/vault/Faults/Robot Gripper.md");
        retrievedByPath.Should().NotBeNull();
        retrievedByPath!.Id.Should().Be(docId);

        // Act 5: Upsert Update (Conflict Resolution)
        doc.ChunkCount = 6;
        doc.Criticality = "Critical";
        doc.Tags.Add("safety");
        await _documentRepository.UpsertAsync(doc);

        var updatedDoc = await _documentRepository.GetByIdAsync(docId);
        updatedDoc.Should().NotBeNull();
        updatedDoc!.ChunkCount.Should().Be(6);
        updatedDoc.Criticality.Should().Be("Critical");
        updatedDoc.Tags.Should().Contain("safety");

        // Act 6: GetAllAsync
        var allDocs = await _documentRepository.GetAllAsync();
        allDocs.Should().HaveCount(1);
        allDocs[0].Id.Should().Be(docId);

        // Act 7: DeleteByIdAsync
        await _documentRepository.DeleteByIdAsync(docId);

        var deletedDoc = await _documentRepository.GetByIdAsync(docId);
        deletedDoc.Should().BeNull();

        int finalCount = await _documentRepository.GetCountAsync();
        finalCount.Should().Be(0);
    }

    [Fact]
    public async Task SqliteSearchHistoryRepository_RecordsQueriesAndRetrievesDescending()
    {
        // Arrange
        await _initializer.InitializeAsync();

        var entry1 = new SearchHistoryEntry
        {
            Id = Guid.NewGuid(),
            Question = "What causes gripper timeout?",
            Answer = "Check proximity sensor I:0/15 and valve manifold pressure.",
            Confidence = ConfidenceLevel.High,
            Sources = new List<SourceReference>
            {
                new(
                    Guid.NewGuid(),
                    "Robot Gripper.md",
                    "Faults/Robot Gripper.md",
                    "Faults",
                    null,
                    "Diagnostic Procedure"
                )
            },
            AskedUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        var entry2 = new SearchHistoryEntry
        {
            Id = Guid.NewGuid(),
            Question = "What is the capital of France?",
            Answer = "The available documentation does not contain sufficient information.",
            Confidence = ConfidenceLevel.Low,
            Sources = new List<SourceReference>(),
            AskedUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        var entry3 = new SearchHistoryEntry
        {
            Id = Guid.NewGuid(),
            Question = "What causes alarm ALM-205?",
            Answer = "Safety gate interlock open on circuit loop E102.",
            Confidence = ConfidenceLevel.High,
            Sources = new List<SourceReference>
            {
                new(
                    Guid.NewGuid(),
                    "ALM-205 Safety Gate Open.md",
                    "Alarms/ALM-205 Safety Gate Open.md",
                    "Alarms",
                    null,
                    "Alarm Description"
                )
            },
            AskedUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        // Act
        await _historyRepository.AddAsync(entry1);
        await _historyRepository.AddAsync(entry2);
        await _historyRepository.AddAsync(entry3);

        var recentList = await _historyRepository.GetRecentAsync(10);

        // Assert: Ordered descending by AskedUtc (entry3 newest, then entry2, then entry1)
        recentList.Should().HaveCount(3);
        recentList[0].Question.Should().Be("What causes alarm ALM-205?");
        recentList[0].Confidence.Should().Be(ConfidenceLevel.High);
        recentList[0].Sources.Should().HaveCount(1);
        recentList[0].Sources[0].FileName.Should().Be("ALM-205 Safety Gate Open.md");

        recentList[1].Question.Should().Be("What is the capital of France?");
        recentList[1].Confidence.Should().Be(ConfidenceLevel.Low);
        recentList[1].Sources.Should().BeEmpty();

        recentList[2].Question.Should().Be("What causes gripper timeout?");
        recentList[2].Confidence.Should().Be(ConfidenceLevel.High);

        // Test limit behavior
        var limitedList = await _historyRepository.GetRecentAsync(2);
        limitedList.Should().HaveCount(2);
        limitedList[0].Question.Should().Be("What causes alarm ALM-205?");
        limitedList[1].Question.Should().Be("What is the capital of France?");
    }

    [Fact]
    public async Task SqliteIndexerStateRepository_ManagesIndexerStatusAndReindexFlag()
    {
        // Arrange
        await _initializer.InitializeAsync();

        // Act 1: Initial state before any write
        var initialState = await _indexerStateRepository.GetStateAsync();
        initialState.Should().NotBeNull();
        initialState.Id.Should().Be(1);
        initialState.Status.Should().Be(IndexerStatus.Idle);
        initialState.ReindexRequested.Should().BeFalse();

        // Act 2: SetReindexRequestedAsync
        await _indexerStateRepository.SetReindexRequestedAsync(true);
        var stateWithReindex = await _indexerStateRepository.GetStateAsync();
        stateWithReindex.ReindexRequested.Should().BeTrue();

        // Act 3: UpdateStatusAsync
        await _indexerStateRepository.UpdateStatusAsync(IndexerStatus.Reindexing);
        var reindexingState = await _indexerStateRepository.GetStateAsync();
        reindexingState.Status.Should().Be(IndexerStatus.Reindexing);

        // Act 4: UpdateStateAsync with completed run
        var completedState = new IndexerState
        {
            Id = 1,
            Status = IndexerStatus.Idle,
            LastRunUtc = DateTimeOffset.UtcNow,
            LastError = null,
            ReindexRequested = false
        };
        await _indexerStateRepository.UpdateStateAsync(completedState);

        var finalState = await _indexerStateRepository.GetStateAsync();
        finalState.Status.Should().Be(IndexerStatus.Idle);
        finalState.ReindexRequested.Should().BeFalse();
        finalState.LastRunUtc.Should().NotBeNull();
        finalState.LastError.Should().BeNull();
    }
}
