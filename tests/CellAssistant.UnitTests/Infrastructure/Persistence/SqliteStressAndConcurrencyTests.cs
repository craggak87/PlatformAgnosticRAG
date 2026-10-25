namespace CellAssistant.UnitTests.Infrastructure.Persistence;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CellAssistant.Domain.Entities;
using CellAssistant.Domain.Enums;
using CellAssistant.Domain.ValueObjects;
using CellAssistant.Infrastructure.Persistence.Sqlite;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

public class SqliteStressAndConcurrencyTests : IDisposable
{
    private readonly string _tempDbDir;
    private readonly string _tempDbPath;
    private readonly SqliteConnectionFactory _diskFactory;
    private readonly SqliteDatabaseInitializer _diskInitializer;

    public SqliteStressAndConcurrencyTests()
    {
        _tempDbDir = Path.Combine(Path.GetTempPath(), $"sqlite_stress_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDbDir);
        _tempDbPath = Path.Combine(_tempDbDir, "metadata.db");

        _diskFactory = new SqliteConnectionFactory(_tempDbPath);
        _diskInitializer = new SqliteDatabaseInitializer(_diskFactory);
        _diskInitializer.Initialize();
    }

    public void Dispose()
    {
        _diskFactory.Dispose();
        SqliteConnection.ClearAllPools();

        try
        {
            if (Directory.Exists(_tempDbDir))
            {
                Directory.Delete(_tempDbDir, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup on temp test artifacts
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Task 1: Empirically verify concurrent read/write in WAL mode (no SQLITE_BUSY crashes)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Concurrency_WalMode_IsActiveOnDiskDatabase()
    {
        using var conn = await _diskFactory.CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode;";
        var mode = await cmd.ExecuteScalarAsync();
        mode.Should().NotBeNull();
        mode!.ToString().Should().BeEquivalentTo("wal");
    }

    [Fact]
    public async Task Concurrency_MultiThreadedReadWriteStress_DoesNotThrowSqliteBusy()
    {
        var docRepo = new SqliteDocumentRepository(_diskFactory);
        var historyRepo = new SqliteSearchHistoryRepository(_diskFactory);
        var stateRepo = new SqliteIndexerStateRepository(_diskFactory);

        // Pre-populate with a few documents
        for (int i = 0; i < 5; i++)
        {
            await docRepo.UpsertAsync(new Document
            {
                Id = Guid.NewGuid(),
                FileName = $"InitDoc_{i}.md",
                FilePath = $"/kb/Manuals/InitDoc_{i}.md",
                RelativePath = $"Manuals/InitDoc_{i}.md",
                Folder = "Manuals",
                ContentHash = $"init_hash_{i}",
                EquipmentId = $"ROBOT0{i + 1}"
            });
        }

        const int workerCount = 16;
        const int iterationsPerWorker = 20;
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        var tasks = Enumerable.Range(0, workerCount).Select(workerId => Task.Run(async () =>
        {
            try
            {
                for (int iter = 0; iter < iterationsPerWorker; iter++)
                {
                    if (workerId % 2 == 0)
                    {
                        // Writers
                        if (iter % 3 == 0)
                        {
                            var docId = Guid.NewGuid();
                            await docRepo.UpsertAsync(new Document
                            {
                                Id = docId,
                                FileName = $"Worker_{workerId}_Iter_{iter}.md",
                                FilePath = $"/kb/Folder/Worker_{workerId}_Iter_{iter}.md",
                                RelativePath = $"Folder/Worker_{workerId}_Iter_{iter}.md",
                                Folder = "Folder",
                                ContentHash = $"hash_{workerId}_{iter}",
                                EquipmentId = $"EQ_{workerId}"
                            });
                        }
                        else if (iter % 3 == 1)
                        {
                            await historyRepo.AddAsync(new SearchHistoryEntry
                            {
                                Id = Guid.NewGuid(),
                                Question = $"Query from worker {workerId} iter {iter}?",
                                Answer = "Answer",
                                Confidence = ConfidenceLevel.Medium,
                                AskedUtc = DateTimeOffset.UtcNow
                            });
                        }
                        else
                        {
                            await stateRepo.UpdateStatusAsync(IndexerStatus.Indexing, $"Worker {workerId}");
                        }
                    }
                    else
                    {
                        // Readers
                        if (iter % 3 == 0)
                        {
                            var docs = await docRepo.GetAllAsync();
                            docs.Should().NotBeNull();
                        }
                        else if (iter % 3 == 1)
                        {
                            var history = await historyRepo.GetRecentAsync(20);
                            history.Should().NotBeNull();
                        }
                        else
                        {
                            var state = await stateRepo.GetStateAsync();
                            state.Should().NotBeNull();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        // Assert no SQLITE_BUSY or other exceptions occurred
        exceptions.Should().BeEmpty("no SQLITE_BUSY or deadlocks should occur during multi-threaded WAL operations");

        // Verify state is consistent
        var finalCount = await docRepo.GetCountAsync();
        finalCount.Should().BeGreaterThan(5);

        var finalHistory = await historyRepo.GetRecentAsync(100);
        finalHistory.Should().NotBeEmpty();

        var finalState = await stateRepo.GetStateAsync();
        finalState.Id.Should().Be(1);
    }

    // ---------------------------------------------------------------------------------------------
    // Task 2: Empirically test SqliteDocumentRepository
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task DocumentRepository_DuplicateFilePath_SameId_UpdatesRecord()
    {
        var docRepo = new SqliteDocumentRepository(_diskFactory);
        var docId = Guid.NewGuid();
        var filePath = "/kb/Equipment/Robotics.md";

        var initialDoc = new Document
        {
            Id = docId,
            FileName = "Robotics.md",
            FilePath = filePath,
            RelativePath = "Equipment/Robotics.md",
            Folder = "Equipment",
            ContentHash = "hash_v1",
            EquipmentId = "ROBOT01",
            ChunkCount = 2,
            Tags = new List<string> { "robot", "v1" }
        };

        await docRepo.UpsertAsync(initialDoc);

        var updatedDoc = new Document
        {
            Id = docId,
            FileName = "Robotics.md",
            FilePath = filePath,
            RelativePath = "Equipment/Robotics.md",
            Folder = "Equipment",
            ContentHash = "hash_v2",
            EquipmentId = "ROBOT02",
            ChunkCount = 5,
            Tags = new List<string> { "robot", "v2", "updated" }
        };

        await docRepo.UpsertAsync(updatedDoc);

        var retrieved = await docRepo.GetByIdAsync(docId);
        retrieved.Should().NotBeNull();
        retrieved!.ContentHash.Should().Be("hash_v2");
        retrieved.EquipmentId.Should().Be("ROBOT02");
        retrieved.ChunkCount.Should().Be(5);
        retrieved.Tags.Should().BeEquivalentTo(new[] { "robot", "v2", "updated" });

        var count = await docRepo.GetCountAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task DocumentRepository_DuplicateFilePath_DifferentId_ThrowsConstraintException()
    {
        var docRepo = new SqliteDocumentRepository(_diskFactory);
        var sharedPath = "/kb/Safety/E-Stop.md";

        var doc1 = new Document
        {
            Id = Guid.NewGuid(),
            FileName = "E-Stop.md",
            FilePath = sharedPath,
            RelativePath = "Safety/E-Stop.md",
            Folder = "Safety",
            ContentHash = "hash_1"
        };
        await docRepo.UpsertAsync(doc1);

        var doc2 = new Document
        {
            Id = Guid.NewGuid(), // Different Guid
            FileName = "E-Stop.md",
            FilePath = sharedPath, // Same unique path
            RelativePath = "Safety/E-Stop.md",
            Folder = "Safety",
            ContentHash = "hash_2"
        };

        var act = async () => await docRepo.UpsertAsync(doc2);

        var exception = await act.Should().ThrowAsync<SqliteException>();
        exception.Which.SqliteErrorCode.Should().Be(19); // SQLITE_CONSTRAINT
        exception.Which.Message.Should().Contain("UNIQUE constraint failed: documents.file_path");
    }

    [Fact]
    public async Task DocumentRepository_NullEquipmentId_HandledAndRetrievedAsNull()
    {
        var docRepo = new SqliteDocumentRepository(_diskFactory);
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            FileName = "NoEquipment.md",
            FilePath = "/kb/General/NoEquipment.md",
            RelativePath = "General/NoEquipment.md",
            Folder = "General",
            ContentHash = "none_hash",
            EquipmentId = null,
            Manufacturer = null,
            Criticality = null
        };

        await docRepo.UpsertAsync(doc);

        var byId = await docRepo.GetByIdAsync(doc.Id);
        byId.Should().NotBeNull();
        byId!.EquipmentId.Should().BeNull();
        byId.Manufacturer.Should().BeNull();
        byId.Criticality.Should().BeNull();

        var byPath = await docRepo.GetByFilePathAsync(doc.FilePath);
        byPath.Should().NotBeNull();
        byPath!.EquipmentId.Should().BeNull();

        // Now update from null to non-null, and back to null
        doc.EquipmentId = "VFD01";
        await docRepo.UpsertAsync(doc);
        (await docRepo.GetByIdAsync(doc.Id))!.EquipmentId.Should().Be("VFD01");

        doc.EquipmentId = null;
        await docRepo.UpsertAsync(doc);
        (await docRepo.GetByIdAsync(doc.Id))!.EquipmentId.Should().BeNull();
    }

    [Theory]
    [InlineData("Equipment/Robotics Cell 1/User Manual v2.0.pdf")]
    [InlineData("Procedures/Wärmebehandlung_Überprüfung_2026.md")]
    [InlineData("Faults/FLT-001 [ALM-205] & Gripper #1 (Rev.3).md")]
    [InlineData("Drawings/O'Connor's \"Emergency\" Circuit.dwg")]
    [InlineData("Nested/10/20/30/Deep/Folder/File.md")]
    [InlineData("International/ロボット_仕様書_2026.pdf")]
    public async Task DocumentRepository_SpecialCharactersInRelativePath_PreservedAccurately(string relativePath)
    {
        var docRepo = new SqliteDocumentRepository(_diskFactory);
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            FileName = Path.GetFileName(relativePath),
            FilePath = $"/kb/{relativePath}",
            RelativePath = relativePath,
            Folder = relativePath.Split('/')[0],
            ContentHash = $"hash_{Guid.NewGuid():N}",
            EquipmentId = "PLC01"
        };

        await docRepo.UpsertAsync(doc);

        var retrieved = await docRepo.GetByIdAsync(doc.Id);
        retrieved.Should().NotBeNull();
        retrieved!.RelativePath.Should().Be(relativePath);

        var retrievedByPath = await docRepo.GetByFilePathAsync(doc.FilePath);
        retrievedByPath.Should().NotBeNull();
        retrievedByPath!.RelativePath.Should().Be(relativePath);
    }

    [Fact]
    public async Task DocumentRepository_LargeTagArrays_PreservedAccurately()
    {
        var docRepo = new SqliteDocumentRepository(_diskFactory);

        // Generate 1000 distinct tags, including special characters, quotes, unicode, brackets
        var tags = new List<string>();
        for (int i = 0; i < 1000; i++)
        {
            tags.Add(i switch
            {
                0 => "tag with \"quotes\"",
                1 => "tag:with:colons",
                2 => "[bracket-tag]",
                3 => "tag/with/slashes",
                4 => "tag with spaces and punctuation!@#$%",
                5 => "über-tag_2026",
                6 => "日本語タグ",
                _ => $"tag_{i:D4}"
            });
        }

        var doc = new Document
        {
            Id = Guid.NewGuid(),
            FileName = "LargeTags.md",
            FilePath = "/kb/Tags/LargeTags.md",
            RelativePath = "Tags/LargeTags.md",
            Folder = "Tags",
            ContentHash = "tags_hash",
            Tags = tags
        };

        await docRepo.UpsertAsync(doc);

        var retrieved = await docRepo.GetByIdAsync(doc.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Tags.Should().HaveCount(1000);
        retrieved.Tags.Should().Equal(tags);
    }

    // ---------------------------------------------------------------------------------------------
    // Task 3: Empirically test SqliteSearchHistoryRepository
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(-999, 20)]
    [InlineData(int.MinValue, 20)]
    public async Task SearchHistoryRepository_ZeroAndNegativeLimits_NormalizeToDefaultTwenty(int requestedLimit, int expectedCount)
    {
        var historyRepo = new SqliteSearchHistoryRepository(_diskFactory);

        // Populate with 30 entries
        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-30);
        for (int i = 0; i < 30; i++)
        {
            await historyRepo.AddAsync(new SearchHistoryEntry
            {
                Id = Guid.NewGuid(),
                Question = $"Question {i}",
                Answer = $"Answer {i}",
                Confidence = ConfidenceLevel.High,
                AskedUtc = baseTime.AddMinutes(i)
            });
        }

        var results = await historyRepo.GetRecentAsync(requestedLimit);
        results.Should().HaveCount(expectedCount);

        // Verify ordering: newest first
        for (int i = 0; i < results.Count - 1; i++)
        {
            results[i].AskedUtc.Should().BeOnOrAfter(results[i + 1].AskedUtc);
        }
    }

    [Theory]
    [InlineData(101, 100)]
    [InlineData(500, 100)]
    [InlineData(int.MaxValue, 100)]
    public async Task SearchHistoryRepository_LimitAboveOneHundred_ClampedToOneHundred(int requestedLimit, int expectedCount)
    {
        var historyRepo = new SqliteSearchHistoryRepository(_diskFactory);

        // Populate with 120 entries
        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-120);
        for (int i = 0; i < 120; i++)
        {
            await historyRepo.AddAsync(new SearchHistoryEntry
            {
                Id = Guid.NewGuid(),
                Question = $"Bulk question {i}",
                Answer = $"Bulk answer {i}",
                Confidence = ConfidenceLevel.Medium,
                AskedUtc = baseTime.AddMinutes(i)
            });
        }

        var results = await historyRepo.GetRecentAsync(requestedLimit);
        results.Should().HaveCount(expectedCount);
    }

    [Fact]
    public async Task SearchHistoryRepository_FiftyPlusSourceCitations_PreservedAccurately()
    {
        var historyRepo = new SqliteSearchHistoryRepository(_diskFactory);

        var sources = new List<SourceReference>();
        for (int i = 1; i <= 65; i++)
        {
            sources.Add(new SourceReference(
                DocumentId: Guid.NewGuid(),
                FileName: $"Manual_Section_{i}.pdf",
                RelativePath: $"Manuals/Section_{i}/Manual_Section_{i}.pdf",
                Folder: "Manuals",
                PageNumber: i,
                SectionHeading: i % 2 == 0 ? $"## 4.{i} Detailed Maintenance & Wiring Guide" : null
            ));
        }

        var entry = new SearchHistoryEntry
        {
            Id = Guid.NewGuid(),
            Question = "What are the comprehensive maintenance procedures across all modules?",
            Answer = "Refer to the attached 65 source references.",
            Confidence = ConfidenceLevel.High,
            Sources = sources,
            AskedUtc = DateTimeOffset.UtcNow
        };

        await historyRepo.AddAsync(entry);

        var recent = await historyRepo.GetRecentAsync(1);
        recent.Should().ContainSingle();

        var retrieved = recent[0];
        retrieved.Id.Should().Be(entry.Id);
        retrieved.Sources.Should().HaveCount(65);

        for (int i = 0; i < 65; i++)
        {
            var expected = sources[i];
            var actual = retrieved.Sources[i];

            actual.DocumentId.Should().Be(expected.DocumentId);
            actual.FileName.Should().Be(expected.FileName);
            actual.RelativePath.Should().Be(expected.RelativePath);
            actual.Folder.Should().Be(expected.Folder);
            actual.PageNumber.Should().Be(expected.PageNumber);
            actual.SectionHeading.Should().Be(expected.SectionHeading);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Task 4: Empirically test SqliteIndexerStateRepository single-row constraint (CHECK (id = 1))
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task IndexerStateRepository_EmptyTable_ReturnsDefaultIdleState()
    {
        var stateRepo = new SqliteIndexerStateRepository(_diskFactory);

        var state = await stateRepo.GetStateAsync();
        state.Should().NotBeNull();
        state.Id.Should().Be(1);
        state.Status.Should().Be(IndexerStatus.Idle);
        state.LastRunUtc.Should().BeNull();
        state.LastError.Should().BeNull();
        state.ReindexRequested.Should().BeFalse();
    }

    [Fact]
    public async Task IndexerStateRepository_MultipleUpdates_MaintainsSingleRow()
    {
        var stateRepo = new SqliteIndexerStateRepository(_diskFactory);

        await stateRepo.UpdateStatusAsync(IndexerStatus.Indexing, "Starting run");
        await stateRepo.SetReindexRequestedAsync(true);
        await stateRepo.UpdateStateAsync(new IndexerState
        {
            Id = 1,
            Status = IndexerStatus.Idle,
            LastRunUtc = DateTimeOffset.UtcNow,
            LastError = null,
            ReindexRequested = false
        });

        using var conn = await _diskFactory.CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM indexer_state;";
        var rowCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        rowCount.Should().Be(1, "indexer_state must always contain exactly one row");

        var state = await stateRepo.GetStateAsync();
        state.Status.Should().Be(IndexerStatus.Idle);
        state.LastRunUtc.Should().NotBeNull();
        state.LastError.Should().BeNull();
        state.ReindexRequested.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-1)]
    [InlineData(100)]
    public async Task IndexerStateRepository_CheckConstraint_RejectsAnyIdOtherThanOne(int invalidId)
    {
        using var conn = await _diskFactory.CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = "INSERT INTO indexer_state (id, status) VALUES (@id, 'Indexing');";
        cmd.Parameters.AddWithValue("@id", invalidId);

        var act = async () => await cmd.ExecuteNonQueryAsync();

        var ex = await act.Should().ThrowAsync<SqliteException>();
        ex.Which.SqliteErrorCode.Should().Be(19); // SQLITE_CONSTRAINT
        ex.Which.Message.Should().Contain("CHECK constraint failed: id = 1");
    }

    [Fact]
    public async Task IndexerStateRepository_CheckConstraint_RejectsUpdatingIdToOtherThanOne()
    {
        var stateRepo = new SqliteIndexerStateRepository(_diskFactory);
        await stateRepo.UpdateStatusAsync(IndexerStatus.Idle);

        using var conn = await _diskFactory.CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = "UPDATE indexer_state SET id = 5 WHERE id = 1;";
        var act = async () => await cmd.ExecuteNonQueryAsync();

        var ex = await act.Should().ThrowAsync<SqliteException>();
        ex.Which.SqliteErrorCode.Should().Be(19); // SQLITE_CONSTRAINT
        ex.Which.Message.Should().Contain("CHECK constraint failed: id = 1");
    }

    [Fact]
    public async Task IndexerStateRepository_ConcurrentUpdates_MaintainSingleRow()
    {
        var stateRepo = new SqliteIndexerStateRepository(_diskFactory);

        var tasks = Enumerable.Range(0, 20).Select(i => Task.Run(async () =>
        {
            if (i % 2 == 0)
            {
                await stateRepo.UpdateStatusAsync(IndexerStatus.Indexing, $"Worker {i}");
            }
            else
            {
                await stateRepo.SetReindexRequestedAsync(i % 4 == 0);
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        using var conn = await _diskFactory.CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM indexer_state;";
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        count.Should().Be(1, "concurrent updates to indexer_state must never create duplicate rows");
    }

    [Fact]
    public async Task Concurrency_HighContentionWritersOnly_DoesNotThrowSqliteBusy()
    {
        var docRepo = new SqliteDocumentRepository(_diskFactory);
        const int writers = 12;
        const int iterations = 10;
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        var tasks = Enumerable.Range(0, writers).Select(w => Task.Run(async () =>
        {
            try
            {
                for (int i = 0; i < iterations; i++)
                {
                    var id = Guid.NewGuid();
                    await docRepo.UpsertAsync(new Document
                    {
                        Id = id,
                        FileName = $"HeavyWriter_{w}_{i}.md",
                        FilePath = $"/kb/Contention/HeavyWriter_{w}_{i}.md",
                        RelativePath = $"Contention/HeavyWriter_{w}_{i}.md",
                        Folder = "Contention",
                        ContentHash = $"hash_{w}_{i}",
                        ChunkCount = i
                    });
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        })).ToArray();

        await Task.WhenAll(tasks);
        exceptions.Should().BeEmpty("busy_timeout=5000 in WAL mode must handle concurrent writer queuing without SQLITE_BUSY");
    }

    [Fact]
    public async Task DocumentRepository_FiveThousandTags_RoundtripsSuccessfully()
    {
        var docRepo = new SqliteDocumentRepository(_diskFactory);
        var tags = Enumerable.Range(0, 5000).Select(i => $"sensor-telemetry-tag-{i:D5}-channel-alpha").ToList();

        var doc = new Document
        {
            Id = Guid.NewGuid(),
            FileName = "FiveThousandTags.md",
            FilePath = "/kb/Tags/FiveThousandTags.md",
            RelativePath = "Tags/FiveThousandTags.md",
            Folder = "Tags",
            ContentHash = "5k_hash",
            Tags = tags
        };

        await docRepo.UpsertAsync(doc);

        var retrieved = await docRepo.GetByIdAsync(doc.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Tags.Should().HaveCount(5000);
        retrieved.Tags[0].Should().Be("sensor-telemetry-tag-00000-channel-alpha");
        retrieved.Tags[4999].Should().Be("sensor-telemetry-tag-04999-channel-alpha");
    }

    [Fact]
    public async Task DocumentRepository_CorruptedTagsJson_FallsBackToEmptyList()
    {
        var docRepo = new SqliteDocumentRepository(_diskFactory);
        var docId = Guid.NewGuid();

        await docRepo.UpsertAsync(new Document
        {
            Id = docId,
            FileName = "CorruptTags.md",
            FilePath = "/kb/CorruptTags.md",
            RelativePath = "CorruptTags.md",
            Folder = "Root",
            ContentHash = "c_hash"
        });

        // Corrupt tags_json with raw SQL
        using var conn = await _diskFactory.CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE documents SET tags_json = '{invalid:json[\"broken' WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", docId.ToString());
        await cmd.ExecuteNonQueryAsync();

        var retrieved = await docRepo.GetByIdAsync(docId);
        retrieved.Should().NotBeNull();
        retrieved!.Tags.Should().NotBeNull();
        retrieved.Tags.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchHistoryRepository_CorruptedSourcesJson_FallsBackToEmptyList()
    {
        var historyRepo = new SqliteSearchHistoryRepository(_diskFactory);
        var id = Guid.NewGuid();

        await historyRepo.AddAsync(new SearchHistoryEntry
        {
            Id = id,
            Question = "Corrupted sources test?",
            Answer = "Answer",
            Confidence = ConfidenceLevel.Medium,
            AskedUtc = DateTimeOffset.UtcNow
        });

        // Corrupt sources_json with raw SQL
        using var conn = await _diskFactory.CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE search_history SET sources_json = '<<<bad_json>>>' WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        await cmd.ExecuteNonQueryAsync();

        var recent = await historyRepo.GetRecentAsync(10);
        var entry = recent.FirstOrDefault(r => r.Id == id);
        entry.Should().NotBeNull();
        entry!.Sources.Should().NotBeNull();
        entry.Sources.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchHistoryRepository_NullOptionalFieldsInSources_RoundtripsSuccessfully()
    {
        var historyRepo = new SqliteSearchHistoryRepository(_diskFactory);
        var sources = new List<SourceReference>
        {
            new SourceReference(
                DocumentId: Guid.NewGuid(),
                FileName: "Notes.md",
                RelativePath: "Procedures/Notes.md",
                Folder: "Procedures",
                PageNumber: null,
                SectionHeading: null
            )
        };

        var entry = new SearchHistoryEntry
        {
            Id = Guid.NewGuid(),
            Question = "Question with null optional source fields?",
            Answer = "Answer",
            Confidence = ConfidenceLevel.Low,
            Sources = sources,
            AskedUtc = DateTimeOffset.UtcNow
        };

        await historyRepo.AddAsync(entry);

        var recent = await historyRepo.GetRecentAsync(5);
        var retrieved = recent.First(r => r.Id == entry.Id);
        retrieved.Sources.Should().ContainSingle();
        retrieved.Sources[0].PageNumber.Should().BeNull();
        retrieved.Sources[0].SectionHeading.Should().BeNull();
    }

    [Fact]
    public async Task IndexerStateRepository_DeletedRow_RecoversDefaultAndReinserts()
    {
        var stateRepo = new SqliteIndexerStateRepository(_diskFactory);
        await stateRepo.UpdateStatusAsync(IndexerStatus.Failed, "Initial error");

        // Force delete the row
        using var conn = await _diskFactory.CreateOpenConnectionAsync();
        using var deleteCmd = conn.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM indexer_state;";
        await deleteCmd.ExecuteNonQueryAsync();

        // 1. Verify default idle state is returned
        var recovered = await stateRepo.GetStateAsync();
        recovered.Should().NotBeNull();
        recovered.Id.Should().Be(1);
        recovered.Status.Should().Be(IndexerStatus.Idle);

        // 2. Verify subsequent update reinserts row 1 successfully
        await stateRepo.UpdateStatusAsync(IndexerStatus.Indexing);

        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM indexer_state WHERE id = 1;";
        var count = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
        count.Should().Be(1);

        var finalState = await stateRepo.GetStateAsync();
        finalState.Status.Should().Be(IndexerStatus.Indexing);
    }
}

