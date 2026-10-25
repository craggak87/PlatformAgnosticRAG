namespace CellAssistant.UnitTests.Api;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Application.DTOs;
using CellAssistant.Domain.Entities;
using CellAssistant.Domain.Enums;
using FluentAssertions;
using NSubstitute;
using Xunit;

public class HistoryAndHealthIntegrationTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _factory;

    public HistoryAndHealthIntegrationTests(ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetHistory_Returns200WithHistoryEntries()
    {
        var client = _factory.CreateClient();
        var entries = new List<SearchHistoryEntry>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Question = "What causes gripper timeout?",
                Answer = "Proximity switch failure.",
                Confidence = ConfidenceLevel.High,
                Sources = new(),
                AskedUtc = DateTimeOffset.UtcNow
            }
        };

        _factory.MockSearchHistoryRepository.GetRecentAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(entries);

        var response = await client.GetAsync("/api/history?limit=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<SearchHistoryDto>>();
        body.Should().NotBeNull();
        body!.Should().HaveCount(1);
        body![0].Question.Should().Be("What causes gripper timeout?");
    }

    [Fact]
    public async Task GetHealth_WhenHealthy_Returns200Ok()
    {
        var client = _factory.CreateClient();
        var components = new Dictionary<string, ComponentHealthDto>
        {
            ["api"] = new("Healthy", "Host up"),
            ["sqlite"] = new("Healthy", "WAL connected"),
            ["qdrant"] = new("Healthy", "Collection ready"),
            ["ollama"] = new("Healthy", "Reachable"),
            ["indexer"] = new("Healthy", "Idle")
        };
        var healthDto = new HealthDto("Healthy", components);

        _factory.MockHealthCheckService.CheckHealthAsync(Arg.Any<CancellationToken>())
            .Returns(healthDto);

        var response = await client.GetAsync("/api/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<HealthDto>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("Healthy");
        body.Api.Should().Be("Healthy");
    }

    [Fact]
    public async Task GetHealth_WhenUnhealthy_Returns503ServiceUnavailable()
    {
        var client = _factory.CreateClient();
        var components = new Dictionary<string, ComponentHealthDto>
        {
            ["api"] = new("Healthy", "Host up"),
            ["sqlite"] = new("Unhealthy", "Disk full"),
            ["qdrant"] = new("Healthy", "Collection ready"),
            ["ollama"] = new("Healthy", "Reachable"),
            ["indexer"] = new("Healthy", "Idle")
        };
        var healthDto = new HealthDto("Unhealthy", components);

        _factory.MockHealthCheckService.CheckHealthAsync(Arg.Any<CancellationToken>())
            .Returns(healthDto);

        var response = await client.GetAsync("/api/health");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadFromJsonAsync<HealthDto>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("Unhealthy");
    }

    [Fact]
    public async Task PostAdminReindex_Returns202Accepted()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/admin/reindex", null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<ReindexResponseDto>();
        body.Should().NotBeNull();
        body!.ReindexRequested.Should().BeTrue();
    }

    [Fact]
    public async Task GetSwaggerJson_Returns200Ok()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Cell Knowledge Assistant API");
    }
}
