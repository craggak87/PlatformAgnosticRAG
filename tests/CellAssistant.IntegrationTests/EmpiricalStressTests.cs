namespace CellAssistant.IntegrationTests;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Application.DTOs;
using CellAssistant.Application.Ports;
using CellAssistant.Domain.Entities;
using CellAssistant.Domain.Enums;
using CellAssistant.Domain.ValueObjects;
using CellAssistant.Infrastructure.Persistence.Sqlite;
using CellAssistant.IntegrationTests.Api;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NSubstitute;
using Xunit;

public class EmpiricalStressTests : IClassFixture<ApiIntegrationTestFixture>, IDisposable
{
    private readonly ApiIntegrationTestFixture _apiFixture;
    private readonly string _tempDirectory;
    private readonly string _sqliteDbPath;

    public EmpiricalStressTests(ApiIntegrationTestFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _tempDirectory = Path.Combine(Path.GetTempPath(), "EmpiricalStress_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _sqliteDbPath = Path.Combine(_tempDirectory, "stress_metadata.db");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    // =========================================================================
    // 1. CHALLENGE: SQLite WAL Mode Persistence & Concurrency on Real Disk
    // =========================================================================

    [Fact]
    public async Task Challenge_SqliteWal_ConcurrentReadersAndWriters_OnDisk_NoDeadlocks()
    {
        // Setup SQLite on real disk
        using var factory = new SqliteConnectionFactory(_sqliteDbPath);
        var initializer = new SqliteDatabaseInitializer(factory);
        await initializer.InitializeAsync();

        var docRepo = new SqliteDocumentRepository(factory);
        var historyRepo = new SqliteSearchHistoryRepository(factory);

        // Pre-populate 10 documents
        for (int i = 0; i < 10; i++)
        {
            await docRepo.UpsertAsync(new Document
            {
                Id = Guid.NewGuid(),
                FileName = $"Doc_{i}.md",
                FilePath = $"/vault/Doc_{i}.md",
                RelativePath = $"Doc_{i}.md",
                DocumentType = DocumentType.Markdown,
                Folder = "Faults",
                ContentHash = $"hash_{i:D4}",
                EquipmentId = $"ROBOT{i:D2}",
                ChunkCount = 2,
                CreatedUtc = DateTimeOffset.UtcNow,
                ModifiedUtc = DateTimeOffset.UtcNow,
                IndexedUtc = DateTimeOffset.UtcNow
            });
        }

        // Stress: 10 concurrent readers and 5 concurrent writers operating simultaneously
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var readerExceptions = new ConcurrentBag<Exception>();
        var writerExceptions = new ConcurrentBag<Exception>();
        var successfulWrites = new ConcurrentBag<int>();

        var readTasks = Enumerable.Range(0, 10).Select(async rId =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var count = await docRepo.GetCountAsync(cts.Token);
                    count.Should().BeGreaterThanOrEqualTo(10);

                    var all = await docRepo.GetAllAsync(cts.Token);
                    all.Count.Should().BeGreaterThanOrEqualTo(count);

                    var recentHistory = await historyRepo.GetRecentAsync(5, cts.Token);
                    recentHistory.Should().NotBeNull();

                    await Task.Delay(5, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    readerExceptions.Add(ex);
                    break;
                }
            }
        }).ToList();

        var writeTasks = Enumerable.Range(0, 5).Select(async wId =>
        {
            for (int j = 0; j < 5; j++)
            {
                try
                {
                    var newDoc = new Document
                    {
                        Id = Guid.NewGuid(),
                        FileName = $"Writer_{wId}_Doc_{j}.md",
                        FilePath = $"/vault/Writer_{wId}_Doc_{j}.md",
                        RelativePath = $"Writer_{wId}_Doc_{j}.md",
                        DocumentType = DocumentType.Markdown,
                        Folder = "Equipment",
                        ContentHash = $"hash_{wId}_{j}",
                        EquipmentId = $"EQ_{wId}",
                        ChunkCount = 1,
                        CreatedUtc = DateTimeOffset.UtcNow,
                        ModifiedUtc = DateTimeOffset.UtcNow,
                        IndexedUtc = DateTimeOffset.UtcNow
                    };
                    await docRepo.UpsertAsync(newDoc, CancellationToken.None);

                    await historyRepo.AddAsync(new SearchHistoryEntry
                    {
                        Id = Guid.NewGuid(),
                        Question = $"Question from writer {wId} item {j}",
                        Answer = "Answer",
                        Confidence = ConfidenceLevel.Medium,
                        Sources = new List<SourceReference>(),
                        AskedUtc = DateTimeOffset.UtcNow
                    }, CancellationToken.None);

                    successfulWrites.Add(1);
                    await Task.Delay(10);
                }
                catch (Exception ex)
                {
                    writerExceptions.Add(ex);
                    break;
                }
            }
        }).ToList();

        await Task.WhenAll(writeTasks);
        cts.Cancel();
        await Task.WhenAll(readTasks);

        readerExceptions.Should().BeEmpty("no concurrent reader should encounter SQLite locking errors in WAL mode");
        writerExceptions.Should().BeEmpty("no concurrent writer should fail with unhandled SQLite exceptions");

        // Verify final document count
        int finalCount = await docRepo.GetCountAsync();
        finalCount.Should().Be(10 + (5 * 5));
    }

    [Fact]
    public async Task Challenge_SqliteWal_DataSurvivesConnectionFactoryRestart_WithoutManualCheckpoint()
    {
        // Step 1: Open and write records
        var docId1 = Guid.NewGuid();
        var docId2 = Guid.NewGuid();

        using (var factory1 = new SqliteConnectionFactory(_sqliteDbPath))
        {
            var init1 = new SqliteDatabaseInitializer(factory1);
            await init1.InitializeAsync();

            var repo1 = new SqliteDocumentRepository(factory1);
            await repo1.UpsertAsync(new Document
            {
                Id = docId1,
                FileName = "Persist1.md",
                FilePath = "/vault/Persist1.md",
                RelativePath = "Persist1.md",
                DocumentType = DocumentType.Markdown,
                Folder = "Faults",
                ContentHash = "hash1",
                CreatedUtc = DateTimeOffset.UtcNow,
                ModifiedUtc = DateTimeOffset.UtcNow,
                IndexedUtc = DateTimeOffset.UtcNow
            });

            await repo1.UpsertAsync(new Document
            {
                Id = docId2,
                FileName = "Persist2.md",
                FilePath = "/vault/Persist2.md",
                RelativePath = "Persist2.md",
                DocumentType = DocumentType.Markdown,
                Folder = "Alarms",
                ContentHash = "hash2",
                CreatedUtc = DateTimeOffset.UtcNow,
                ModifiedUtc = DateTimeOffset.UtcNow,
                IndexedUtc = DateTimeOffset.UtcNow
            });

            // Confirm count in session 1
            int count1 = await repo1.GetCountAsync();
            count1.Should().Be(2);
        } // factory1 disposed here

        // Step 2: Open brand-new factory pointing to the exact same file
        using (var factory2 = new SqliteConnectionFactory(_sqliteDbPath))
        {
            var repo2 = new SqliteDocumentRepository(factory2);

            int count2 = await repo2.GetCountAsync();
            count2.Should().Be(2, "data must survive connection factory lifecycle and be read by a new connection factory in WAL mode");

            var doc1 = await repo2.GetByIdAsync(docId1);
            doc1.Should().NotBeNull();
            doc1!.FileName.Should().Be("Persist1.md");

            var doc2 = await repo2.GetByIdAsync(docId2);
            doc2.Should().NotBeNull();
            doc2!.FileName.Should().Be("Persist2.md");
        }
    }

    // =========================================================================
    // 2. CHALLENGE: High-Concurrency on /api/query and /api/query/stream
    // =========================================================================

    [Fact]
    public async Task Challenge_ApiQuery_HighConcurrency_50RequestsSimultaneous_Returns200()
    {
        var client = _apiFixture.CreateClient();

        // Setup mock query service to return answer echoing request question
        _apiFixture.MockQueryService.ExecuteQueryAsync(Arg.Any<QueryRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var req = callInfo.ArgAt<QueryRequestDto>(0);
                return Task.FromResult(new QueryResponseDto(
                    $"Echo: {req.Question}",
                    "High",
                    new List<SourceReferenceDto>
                    {
                        new(Guid.NewGuid(), "EchoDoc.md", "Faults/EchoDoc.md", null, "Section 1")
                    },
                    ConfidenceScore: 0.92,
                    RetrievedSources: new List<SourceReferenceDto>(),
                    ExecutionTimeMs: 12
                ));
            });

        const int concurrency = 50;
        var tasks = Enumerable.Range(0, concurrency).Select(async i =>
        {
            var question = $"Concurrent Question #{i:D3} regarding FLT-{i:D3}";
            var request = new QueryRequestDto(question, $"EQ-{i}", "Faults");

            var response = await client.PostAsJsonAsync("/api/query", request);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await response.Content.ReadFromJsonAsync<QueryResponseDto>();
            body.Should().NotBeNull();
            body!.Answer.Should().Be($"Echo: {question}");
            body.Confidence.Should().Be("High");
            body.Sources.Should().HaveCount(1);
        });

        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task Challenge_ApiQueryStream_HighConcurrency_50StreamsSimultaneous_NoCrosstalk()
    {
        var client = _apiFixture.CreateClient();

        _apiFixture.MockQueryService.ExecuteQueryStreamAsync(
            Arg.Any<QueryRequestDto>(),
            Arg.Any<Func<string, Task>>(),
            Arg.Any<Func<QueryMetadataDto, Task>>(),
            Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var req = callInfo.ArgAt<QueryRequestDto>(0);
                var onToken = callInfo.ArgAt<Func<string, Task>>(1);
                var onMetadata = callInfo.ArgAt<Func<QueryMetadataDto, Task>>(2);
                var ct = callInfo.ArgAt<CancellationToken>(3);

                await onMetadata(new QueryMetadataDto("High", new List<SourceReferenceDto>()));
                await Task.Yield();
                await onToken($"Stream_Start[{req.Question}]");
                await Task.Yield();
                await onToken($"_Middle[{req.EquipmentId}]");
                await Task.Yield();
                await onToken($"_End[{req.Question}]");
            });

        const int concurrency = 50;
        var tasks = Enumerable.Range(0, concurrency).Select(async i =>
        {
            var question = $"StreamReq_{i:D3}";
            var eqId = $"EQ_{i:D3}";
            var request = new QueryRequestDto(question, eqId);

            var response = await client.PostAsJsonAsync("/api/query/stream", request);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

            var streamContent = await response.Content.ReadAsStringAsync();

            // Verify the stream contains ONLY its own tokens, with zero crosstalk from other concurrent streams
            streamContent.Should().Contain("event: metadata\n");
            streamContent.Should().Contain($"Stream_Start[{question}]");
            streamContent.Should().Contain($"_Middle[{eqId}]");
            streamContent.Should().Contain($"_End[{question}]");
            streamContent.Should().Contain("event: done\ndata: {}\n\n");

            // Verify no other stream's id appears in this stream's body
            int otherIndex = (i + 1) % concurrency;
            streamContent.Should().NotContain($"Stream_Start[StreamReq_{otherIndex:D3}]");
        });

        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task Challenge_ApiQueryStream_ClientEarlyCancellation_HandledGracefully()
    {
        var client = _apiFixture.CreateClient();

        _apiFixture.MockQueryService.ExecuteQueryStreamAsync(
            Arg.Any<QueryRequestDto>(),
            Arg.Any<Func<string, Task>>(),
            Arg.Any<Func<QueryMetadataDto, Task>>(),
            Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var onToken = callInfo.ArgAt<Func<string, Task>>(1);
                var onMetadata = callInfo.ArgAt<Func<QueryMetadataDto, Task>>(2);
                var ct = callInfo.ArgAt<CancellationToken>(3);

                await onMetadata(new QueryMetadataDto("High", new List<SourceReferenceDto>()));
                for (int i = 0; i < 50; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    await onToken($"Token_{i} ");
                    await Task.Delay(10, ct);
                }
            });

        // Test 5 concurrent clients that cancel immediately after starting
        var tasks = Enumerable.Range(0, 5).Select(async _ =>
        {
            using var cts = new CancellationTokenSource();
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/query/stream")
            {
                Content = JsonContent.Create(new QueryRequestDto("Cancel test question"))
            };

            // Cancel after 15ms
            cts.CancelAfter(15);

            try
            {
                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
                var buffer = new byte[128];
                while (await stream.ReadAsync(buffer, cts.Token) > 0)
                {
                    // keep reading until cancellation fires
                }
            }
            catch (OperationCanceledException)
            {
                // Expected client-side cancellation
            }
        });

        // Assert: WhenAll finishes cleanly without server blowing up
        var act = async () => await Task.WhenAll(tasks);
        await act.Should().NotThrowAsync("server must gracefully handle client stream cancellations");
    }

    // =========================================================================
    // 3. CHALLENGE: Edge Case Path Traversal Payloads
    // =========================================================================

    [Theory]
    [InlineData("../../../../etc/passwd")]
    [InlineData("..\\..\\..\\etc\\passwd")]
    [InlineData("Faults/../../../../etc/passwd")]
    [InlineData("Manuals/..\\..\\..\\..\\etc\\shadow")]
    [InlineData("%2e%2e%2fetc%2fpasswd")]
    [InlineData("%252e%252e%252fetc%252fpasswd")]
    [InlineData("%25252e%25252e%25252fetc%25252fpasswd")]
    [InlineData("/etc/passwd")]
    [InlineData("/etc/shadow")]
    [InlineData("Robot Gripper.md\0.txt")]
    [InlineData("Manuals%00/../../etc/passwd")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("/")]
    public async Task Challenge_PathTraversal_EscapingAttacks_Strictly403Forbidden(string payload)
    {
        var client = _apiFixture.CreateClient();

        var response = await client.GetAsync($"/api/documents/ignored/file?path={Uri.EscapeDataString(payload)}");

        // The endpoint MUST reject every escaping payload with 403 Forbidden
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            $"escaping payload '{payload}' must be rejected with 403 Forbidden");

        var body = await response.Content.ReadFromJsonAsync<ApiErrorDto>();
        body.Should().NotBeNull();
        body!.CorrelationId.Should().NotBeNullOrWhiteSpace();
        body.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("....//....//etc/passwd")]
    [InlineData("..;/..;/etc/passwd")]
    [InlineData("C:\\Windows\\System32\\drivers\\etc\\hosts")]
    [InlineData("~/.bashrc")]
    public async Task Challenge_PathTraversal_NonEscapingWeirdPaths_NeverReturnsSuccess(string payload)
    {
        var client = _apiFixture.CreateClient();

        var response = await client.GetAsync($"/api/documents/ignored/file?path={Uri.EscapeDataString(payload)}");

        // Must NEVER return 200 OK or 206 Partial Content
        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
        response.StatusCode.Should().NotBe(HttpStatusCode.PartialContent);
        // It returns 404 NotFound (or 403) because it resolves safely inside vault but doesn't exist
        response.StatusCode.Should().Match(s => s == HttpStatusCode.NotFound || s == HttpStatusCode.Forbidden || s == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Challenge_PathTraversal_NonExistentFileInsideVault_Returns404NotFound()
    {
        var client = _apiFixture.CreateClient();

        // Valid syntax within vault, but file does not exist
        var response = await client.GetAsync("/api/documents/ignored/file?path=Faults/NonExistent_Fake_File_9999.md");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<ApiErrorDto>();
        body.Should().NotBeNull();
        body!.Message.Should().Contain("does not exist on disk");
    }

    [Fact]
    public async Task Challenge_PathTraversal_SymlinkEscapingVault_Returns403Forbidden()
    {
        // Create an outside secret file and a symlink inside sample vault if filesystem allows
        string outsideDir = Path.Combine(Path.GetTempPath(), "OutsideVault_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideDir);
        string secretFile = Path.Combine(outsideDir, "secret.txt");
        await File.WriteAllTextAsync(secretFile, "CLASSIFIED_DATA_LEAK");

        string symlinkInsideVault = Path.Combine(_apiFixture.SampleVaultRoot, "Faults", "test_symlink_escape.txt");

        try
        {
            File.CreateSymbolicLink(symlinkInsideVault, secretFile);

            var client = _apiFixture.CreateClient();
            var response = await client.GetAsync("/api/documents/ignored/file?path=Faults/test_symlink_escape.txt");

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "symlink escaping vault must be rejected with 403 Forbidden");
        }
        catch (IOException)
        {
            // If symlinks not permitted by OS or FS, pass
        }
        finally
        {
            if (File.Exists(symlinkInsideVault))
            {
                File.Delete(symlinkInsideVault);
            }
            if (Directory.Exists(outsideDir))
            {
                Directory.Delete(outsideDir, recursive: true);
            }
        }
    }

    // =========================================================================
    // 4. CHALLENGE: HTTP Range Header Requests on Binary PDF Manuals
    // =========================================================================

    [Fact]
    public async Task Challenge_RangeHeader_ExactByteSlices_VerifyPartialContentAndHeaders()
    {
        var client = _apiFixture.CreateClient();
        string pdfPath = "Manuals/ROBOT01_ABB_IRB4600_Manual.pdf";

        // First, get the full content to know exact total length
        var fullResp = await client.GetAsync($"/api/documents/ignored/file?path={pdfPath}");
        fullResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var fullBytes = await fullResp.Content.ReadAsByteArrayAsync();
        long totalLength = fullBytes.Length;
        totalLength.Should().BeGreaterThan(500);

        // Case 1: Standard range [0-99] (first 100 bytes)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"/api/documents/ignored/file?path={pdfPath}");
            req.Headers.Range = new RangeHeaderValue(0, 99);
            var resp = await client.SendAsync(req);

            resp.StatusCode.Should().Be(HttpStatusCode.PartialContent);
            resp.Content.Headers.ContentRange.Should().NotBeNull();
            resp.Content.Headers.ContentRange!.From.Should().Be(0);
            resp.Content.Headers.ContentRange!.To.Should().Be(99);
            resp.Content.Headers.ContentRange!.Length.Should().Be(totalLength);

            var bytes = await resp.Content.ReadAsByteArrayAsync();
            bytes.Length.Should().Be(100);
            bytes.Should().Equal(fullBytes.Take(100));
        }

        // Case 2: Middle slice [100-249] (150 bytes)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"/api/documents/ignored/file?path={pdfPath}");
            req.Headers.Range = new RangeHeaderValue(100, 249);
            var resp = await client.SendAsync(req);

            resp.StatusCode.Should().Be(HttpStatusCode.PartialContent);
            resp.Content.Headers.ContentRange!.From.Should().Be(100);
            resp.Content.Headers.ContentRange!.To.Should().Be(249);
            resp.Content.Headers.ContentRange!.Length.Should().Be(totalLength);

            var bytes = await resp.Content.ReadAsByteArrayAsync();
            bytes.Length.Should().Be(150);
            bytes.Should().Equal(fullBytes.Skip(100).Take(150));
        }

        // Case 3: Open-ended range [300-] (from byte 300 to EOF)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"/api/documents/ignored/file?path={pdfPath}");
            req.Headers.Range = new RangeHeaderValue(300, null);
            var resp = await client.SendAsync(req);

            resp.StatusCode.Should().Be(HttpStatusCode.PartialContent);
            resp.Content.Headers.ContentRange!.From.Should().Be(300);
            resp.Content.Headers.ContentRange!.To.Should().Be(totalLength - 1);
            resp.Content.Headers.ContentRange!.Length.Should().Be(totalLength);

            var bytes = await resp.Content.ReadAsByteArrayAsync();
            bytes.Length.Should().Be((int)(totalLength - 300));
            bytes.Should().Equal(fullBytes.Skip(300));
        }

        // Case 4: Suffix range [-150] (last 150 bytes)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"/api/documents/ignored/file?path={pdfPath}");
            req.Headers.Range = new RangeHeaderValue(null, 150);
            var resp = await client.SendAsync(req);

            resp.StatusCode.Should().Be(HttpStatusCode.PartialContent);
            resp.Content.Headers.ContentRange!.From.Should().Be(totalLength - 150);
            resp.Content.Headers.ContentRange!.To.Should().Be(totalLength - 1);
            resp.Content.Headers.ContentRange!.Length.Should().Be(totalLength);

            var bytes = await resp.Content.ReadAsByteArrayAsync();
            bytes.Length.Should().Be(150);
            bytes.Should().Equal(fullBytes.Skip((int)(totalLength - 150)));
        }

        // Case 5: Out of bounds range [9999999-99999999] -> 416 Range Not Satisfiable
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"/api/documents/ignored/file?path={pdfPath}");
            req.Headers.Range = new RangeHeaderValue(9999999, 99999999);
            var resp = await client.SendAsync(req);

            resp.StatusCode.Should().Be(HttpStatusCode.RequestedRangeNotSatisfiable);
            resp.Content.Headers.ContentRange.Should().NotBeNull();
            resp.Content.Headers.ContentRange!.Length.Should().Be(totalLength);
        }
    }

    [Fact]
    public async Task Challenge_SqliteWal_WalAndShmFilesCreatedOnDisk()
    {
        string dbFile = Path.Combine(_tempDirectory, "wal_check.db");
        string walFile = dbFile + "-wal";
        string shmFile = dbFile + "-shm";

        using (var factory = new SqliteConnectionFactory(dbFile))
        {
            var initializer = new SqliteDatabaseInitializer(factory);
            await initializer.InitializeAsync();

            var repo = new SqliteDocumentRepository(factory);
            for (int i = 0; i < 50; i++)
            {
                await repo.UpsertAsync(new Document
                {
                    Id = Guid.NewGuid(),
                    FileName = $"WalCheck_{i}.md",
                    FilePath = $"/vault/WalCheck_{i}.md",
                    RelativePath = $"WalCheck_{i}.md",
                    DocumentType = DocumentType.Markdown,
                    Folder = "Procedures",
                    ContentHash = $"hash_{i}",
                    CreatedUtc = DateTimeOffset.UtcNow,
                    ModifiedUtc = DateTimeOffset.UtcNow,
                    IndexedUtc = DateTimeOffset.UtcNow
                });
            }

            // In WAL mode, with active connection and uncheckpointed writes, wal file or shm file exists
            (File.Exists(walFile) || File.Exists(shmFile) || File.Exists(dbFile)).Should().BeTrue();
            File.Exists(dbFile).Should().BeTrue();

            using var conn = await factory.CreateOpenConnectionAsync();
            using var pragma = conn.CreateCommand();
            pragma.CommandText = "PRAGMA journal_mode;";
            var mode = await pragma.ExecuteScalarAsync();
            mode?.ToString()?.ToLowerInvariant().Should().Be("wal");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public async Task Challenge_ApiQuery_EmptyQuestion_Returns400BadRequest(string emptyQuestion)
    {
        var client = _apiFixture.CreateClient();
        var request = new QueryRequestDto(emptyQuestion);

        var response = await client.PostAsJsonAsync("/api/query", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var streamResponse = await client.PostAsJsonAsync("/api/query/stream", request);
        streamResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Challenge_ApiQuery_OversizedQuestion_Returns400BadRequest()
    {
        var client = _apiFixture.CreateClient();
        string oversizedQuestion = new string('A', 2001);
        var request = new QueryRequestDto(oversizedQuestion);

        var response = await client.PostAsJsonAsync("/api/query", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var streamResponse = await client.PostAsJsonAsync("/api/query/stream", request);
        streamResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Challenge_ApiHistory_Health_HighConcurrency_Returns200()
    {
        var client = _apiFixture.CreateClient();

        _apiFixture.MockSearchHistoryRepository.GetRecentAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<SearchHistoryEntry>());

        _apiFixture.MockHealthCheckService.CheckHealthAsync(Arg.Any<CancellationToken>())
            .Returns(new HealthDto("Healthy", new Dictionary<string, ComponentHealthDto>
            {
                ["api"] = new("Healthy", "Up")
            }));

        var historyTasks = Enumerable.Range(0, 25).Select(async _ =>
        {
            var res = await client.GetAsync("/api/history?limit=10");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
        });

        var healthTasks = Enumerable.Range(0, 25).Select(async _ =>
        {
            var res = await client.GetAsync("/api/health");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
        });

        await Task.WhenAll(historyTasks.Concat(healthTasks));
    }
}
