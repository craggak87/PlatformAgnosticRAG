namespace CellAssistant.IntegrationTests.Api;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Application.DTOs;
using CellAssistant.Application.Ports;
using CellAssistant.Domain.Entities;
using CellAssistant.Domain.Enums;
using CellAssistant.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Xunit;

public class ApiIntegrationTestFixture : WebApplicationFactory<Program>
{
    public IQueryService MockQueryService { get; } = Substitute.For<IQueryService>();
    public IDocumentRepository MockDocumentRepository { get; } = Substitute.For<IDocumentRepository>();
    public ISearchHistoryRepository MockSearchHistoryRepository { get; } = Substitute.For<ISearchHistoryRepository>();
    public IIndexerStateRepository MockIndexerStateRepository { get; } = Substitute.For<IIndexerStateRepository>();
    public IDocumentIndexer MockDocumentIndexer { get; } = Substitute.For<IDocumentIndexer>();
    public IHealthCheckService MockHealthCheckService { get; } = Substitute.For<IHealthCheckService>();

    public string SampleVaultRoot { get; } = ResolveSampleVaultPath();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["KnowledgeBase:RootPath"] = SampleVaultRoot
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IQueryService>();
            services.AddSingleton(MockQueryService);

            services.RemoveAll<IDocumentRepository>();
            services.AddSingleton(MockDocumentRepository);

            services.RemoveAll<ISearchHistoryRepository>();
            services.AddSingleton(MockSearchHistoryRepository);

            services.RemoveAll<IIndexerStateRepository>();
            services.AddSingleton(MockIndexerStateRepository);

            services.RemoveAll<IDocumentIndexer>();
            services.AddSingleton(MockDocumentIndexer);

            services.RemoveAll<IHealthCheckService>();
            services.AddSingleton(MockHealthCheckService);
        });
    }

    private static string ResolveSampleVaultPath()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(current, "sample-data", "KnowledgeBase");
            if (Directory.Exists(candidate))
                return Path.GetFullPath(candidate);
            current = Directory.GetParent(current)?.FullName;
        }

        return Path.GetFullPath("sample-data/KnowledgeBase");
    }
}

public class ApiIntegrationTests : IClassFixture<ApiIntegrationTestFixture>
{
    private readonly ApiIntegrationTestFixture _factory;

    public ApiIntegrationTests(ApiIntegrationTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostQuery_SyncQuery_ReturnsAnswerConfidenceAndSources()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new QueryRequestDto("What causes gripper timeout?", "ROBOT01", "Faults");

        var sourceDocId = Guid.NewGuid();
        var sources = new List<SourceReferenceDto>
        {
            new(
                sourceDocId,
                "Robot Gripper.md",
                "Faults/Robot Gripper.md",
                null,
                "Diagnostic Procedure"
            )
        };

        var expectedResponse = new QueryResponseDto(
            "Gripper timeout is caused by proximity switch I:0/15 failure or pneumatic pressure drop below 5.5 bar.",
            "High",
            sources,
            ConfidenceScore: 0.88,
            RetrievedSources: sources,
            ExecutionTimeMs: 42
        );

        _factory.MockQueryService.ExecuteQueryAsync(Arg.Any<QueryRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(expectedResponse);

        // Act
        var response = await client.PostAsJsonAsync("/api/query", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var body = await response.Content.ReadFromJsonAsync<QueryResponseDto>();
        body.Should().NotBeNull();
        body!.Answer.Should().Contain("I:0/15");
        body.Confidence.Should().Be("High");
        body.ConfidenceScore.Should().Be(0.88);
        body.Sources.Should().HaveCount(1);
        body.Sources[0].FileName.Should().Be("Robot Gripper.md");
        body.Sources[0].RelativePath.Should().Be("Faults/Robot Gripper.md");
        body.Sources[0].SectionHeading.Should().Be("Diagnostic Procedure");
    }

    [Fact]
    public async Task PostQuery_OutOfScope_ReturnsVerbatimFallbackString()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new QueryRequestDto("What is the capital of France?");

        var expectedResponse = new QueryResponseDto(
            "The available documentation does not contain sufficient information.",
            "Low",
            new List<SourceReferenceDto>(),
            ConfidenceScore: 0.12,
            RetrievedSources: new List<SourceReferenceDto>(),
            ExecutionTimeMs: 15
        );

        _factory.MockQueryService.ExecuteQueryAsync(Arg.Any<QueryRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(expectedResponse);

        // Act
        var response = await client.PostAsJsonAsync("/api/query", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<QueryResponseDto>();
        body.Should().NotBeNull();
        body!.Answer.Should().Be("The available documentation does not contain sufficient information.");
        body.Confidence.Should().Be("Low");
        body.Sources.Should().BeEmpty();
    }

    [Fact]
    public async Task PostQueryStream_SSEStreaming_EmitsMetadataTokenAndDone()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new QueryRequestDto("Explain alarm ALM-205");

        var sources = new List<SourceReferenceDto>
        {
            new(
                Guid.NewGuid(),
                "ALM-205 Safety Gate Open.md",
                "Alarms/ALM-205 Safety Gate Open.md",
                null,
                "Alarm Description"
            )
        };

        _factory.MockQueryService.ExecuteQueryStreamAsync(
            Arg.Any<QueryRequestDto>(),
            Arg.Any<Func<string, Task>>(),
            Arg.Any<Func<QueryMetadataDto, Task>>(),
            Arg.Any<CancellationToken>())
            .Returns(async info =>
            {
                var onToken = info.ArgAt<Func<string, Task>>(1);
                var onMetadata = info.ArgAt<Func<QueryMetadataDto, Task>>(2);

                await onMetadata(new QueryMetadataDto("High", sources));
                await onToken("ALM-205 ");
                await onToken("indicates ");
                await onToken("safety gate open.");
            });

        // Act
        var response = await client.PostAsJsonAsync("/api/query/stream", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        var responseText = await response.Content.ReadAsStringAsync();
        responseText.Should().Contain("event: metadata\n");
        responseText.Should().Contain("ALM-205 Safety Gate Open.md");
        responseText.Should().Contain("event: token\n");
        responseText.Should().Contain("ALM-205 ");
        responseText.Should().Contain("safety gate open.");
        responseText.Should().Contain("event: done\ndata: {}\n\n");
    }

    [Fact]
    public async Task GetDocumentFile_ValidMarkdown_StreamsContentWithRangeSupport()
    {
        // Arrange
        var client = _factory.CreateClient();
        string relativePath = "Faults/Robot Gripper.md";

        // Act 1: Full content request
        var fullResponse = await client.GetAsync($"/api/documents/ignored/file?path={relativePath}");

        // Assert 1: Full content
        fullResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        fullResponse.Content.Headers.ContentType?.MediaType.Should().Be("text/plain");
        fullResponse.Headers.Contains("Accept-Ranges").Should().BeTrue();
        fullResponse.Content.Headers.ContentDisposition?.FileName.Should().Be("\"Robot Gripper.md\"");

        var fullText = await fullResponse.Content.ReadAsStringAsync();
        fullText.Should().Contain("Robot Gripper");

        // Act 2: Byte range request (HTTP 206 Partial Content)
        var rangeRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/documents/ignored/file?path={relativePath}");
        rangeRequest.Headers.Range = new RangeHeaderValue(0, 19);

        var rangeResponse = await client.SendAsync(rangeRequest);

        // Assert 2: Partial content
        rangeResponse.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        rangeResponse.Content.Headers.ContentRange.Should().NotBeNull();
        rangeResponse.Content.Headers.ContentRange!.From.Should().Be(0);
        rangeResponse.Content.Headers.ContentRange!.To.Should().Be(19);

        var rangeBytes = await rangeResponse.Content.ReadAsByteArrayAsync();
        rangeBytes.Length.Should().Be(20);
    }

    [Fact]
    public async Task GetDocumentFile_ValidPdf_StreamsBinaryPdfWithRangeSupport()
    {
        // Arrange
        var client = _factory.CreateClient();
        string relativePath = "Manuals/ROBOT01_ABB_IRB4600_Manual.pdf";

        // Act 1: Full PDF request
        var fullResponse = await client.GetAsync($"/api/documents/ignored/file?path={relativePath}");

        // Assert 1: Full content
        fullResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        fullResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");
        fullResponse.Headers.Contains("Accept-Ranges").Should().BeTrue();
        fullResponse.Content.Headers.ContentDisposition?.FileName.Should().Be("\"ROBOT01_ABB_IRB4600_Manual.pdf\"");

        var fullBytes = await fullResponse.Content.ReadAsByteArrayAsync();
        fullBytes.Length.Should().BeGreaterThan(100);
        fullBytes[0].Should().Be((byte)'%');
        fullBytes[1].Should().Be((byte)'P');
        fullBytes[2].Should().Be((byte)'D');
        fullBytes[3].Should().Be((byte)'F');

        // Act 2: Byte range request (HTTP 206 Partial Content)
        var rangeRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/documents/ignored/file?path={relativePath}");
        rangeRequest.Headers.Range = new RangeHeaderValue(0, 49);

        var rangeResponse = await client.SendAsync(rangeRequest);

        // Assert 2: Partial content
        rangeResponse.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        rangeResponse.Content.Headers.ContentRange.Should().NotBeNull();
        rangeResponse.Content.Headers.ContentRange!.From.Should().Be(0);
        rangeResponse.Content.Headers.ContentRange!.To.Should().Be(49);

        var rangeBytes = await rangeResponse.Content.ReadAsByteArrayAsync();
        rangeBytes.Length.Should().Be(50);
        rangeBytes[0].Should().Be((byte)'%');
        rangeBytes[1].Should().Be((byte)'P');
        rangeBytes[2].Should().Be((byte)'D');
        rangeBytes[3].Should().Be((byte)'F');
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\..\\etc\\passwd")]
    [InlineData("%252e%252e%252fetc%252fpasswd")]
    [InlineData("%252e%252e%252f%252e%252e%252fetc%252fpasswd")]
    [InlineData("Manuals/Robot.pdf\0/../../etc/passwd")]
    [InlineData("Manuals/Robot.pdf%00/../../etc/passwd")]
    [InlineData("/etc/passwd")]
    public async Task GetDocumentFile_PathTraversalAttempts_Return403Forbidden(string maliciousPath)
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/api/documents/ignored/file?path={Uri.EscapeDataString(maliciousPath)}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            $"Adversarial payload '{maliciousPath}' must be rejected with 403 Forbidden");

        var err = await response.Content.ReadFromJsonAsync<ApiErrorDto>();
        err.Should().NotBeNull();
        err!.CorrelationId.Should().NotBeNullOrWhiteSpace();
        err.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetHistory_ReturnsSearchAuditLog()
    {
        // Arrange
        var client = _factory.CreateClient();

        var historyEntries = new List<SearchHistoryEntry>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Question = "What causes gripper timeout?",
                Answer = "Sensor I:0/15 failure",
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
                AskedUtc = DateTimeOffset.UtcNow
            }
        };

        _factory.MockSearchHistoryRepository.GetRecentAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(historyEntries);

        // Act
        var response = await client.GetAsync("/api/history?limit=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<SearchHistoryDto>>();
        body.Should().NotBeNull();
        body!.Should().HaveCount(1);
        body![0].Question.Should().Be("What causes gripper timeout?");
        body[0].Confidence.Should().Be("High");
        body[0].Sources.Should().HaveCount(1);
        body[0].Sources[0].FileName.Should().Be("Robot Gripper.md");
    }

    [Theory]
    [InlineData("Healthy", HttpStatusCode.OK)]
    [InlineData("Unhealthy", HttpStatusCode.ServiceUnavailable)]
    public async Task GetHealth_ReturnsExpectedStatus(string status, HttpStatusCode expectedStatusCode)
    {
        // Arrange
        var client = _factory.CreateClient();

        var components = new Dictionary<string, ComponentHealthDto>
        {
            ["api"] = new("Healthy", "Host up"),
            ["sqlite"] = new(status, status == "Healthy" ? "Connected" : "Failed"),
            ["qdrant"] = new("Healthy", "Collection ready"),
            ["ollama"] = new("Healthy", "Reachable"),
            ["indexer"] = new("Healthy", "Idle")
        };

        var healthDto = new HealthDto(status, components);
        _factory.MockHealthCheckService.CheckHealthAsync(Arg.Any<CancellationToken>())
            .Returns(healthDto);

        // Act
        var response = await client.GetAsync("/api/health");

        // Assert
        response.StatusCode.Should().Be(expectedStatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthDto>();
        body.Should().NotBeNull();
        body!.Status.Should().Be(status);
    }

    [Fact]
    public async Task PostAdminReindex_Returns202Accepted()
    {
        // Arrange
        var client = _factory.CreateClient();

        _factory.MockDocumentIndexer.IndexAllAsync(Arg.Any<CancellationToken>())
            .Returns(new IndexerRunResult(26, 26, 0, 0, 0, TimeSpan.FromMilliseconds(50), true));

        // Act
        var response = await client.PostAsync("/api/admin/reindex", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<ReindexResponseDto>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("Reindex requested successfully");
        body.ReindexRequested.Should().BeTrue();
    }
}
