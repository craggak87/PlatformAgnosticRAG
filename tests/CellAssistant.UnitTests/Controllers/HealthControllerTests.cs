namespace CellAssistant.UnitTests.Controllers;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Api.Controllers;
using CellAssistant.Application.DTOs;
using CellAssistant.Application.Ports;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

public class HealthControllerTests
{
    private readonly IHealthCheckService _healthService = Substitute.For<IHealthCheckService>();
    private readonly HealthController _controller;

    public HealthControllerTests()
    {
        _controller = new HealthController(_healthService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    [Fact]
    public async Task GetHealthAsync_WhenHealthy_Returns200Ok()
    {
        var components = new Dictionary<string, ComponentHealthDto>
        {
            ["api"] = new("Healthy", "API host running"),
            ["sqlite"] = new("Healthy", "SQLite connected"),
            ["qdrant"] = new("Healthy", "Qdrant ready"),
            ["ollama"] = new("Healthy", "Ollama reachable"),
            ["indexer"] = new("Healthy", "Watcher idle")
        };
        var healthDto = new HealthDto("Healthy", components);

        _healthService.CheckHealthAsync(Arg.Any<CancellationToken>()).Returns(healthDto);

        var result = await _controller.GetHealthAsync();

        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(StatusCodes.Status200OK);
        var health = okResult.Value as HealthDto;
        health.Should().NotBeNull();
        health!.Status.Should().Be("Healthy");
        health.Api.Should().Be("Healthy");
        health.Sqlite.Should().Be("Healthy");
        health.Qdrant.Should().Be("Healthy");
        health.Ollama.Should().Be("Healthy");
        health.Indexer.Should().Be("Healthy");
    }

    [Fact]
    public async Task GetHealthAsync_WhenUnhealthy_Returns503ServiceUnavailable()
    {
        var components = new Dictionary<string, ComponentHealthDto>
        {
            ["api"] = new("Healthy", "API host running"),
            ["sqlite"] = new("Healthy", "SQLite connected"),
            ["qdrant"] = new("Unhealthy", "Qdrant connection refused"),
            ["ollama"] = new("Healthy", "Ollama reachable"),
            ["indexer"] = new("Healthy", "Watcher idle")
        };
        var healthDto = new HealthDto("Unhealthy", components);

        _healthService.CheckHealthAsync(Arg.Any<CancellationToken>()).Returns(healthDto);

        var result = await _controller.GetHealthAsync();

        var statusResult = result as ObjectResult;
        statusResult.Should().NotBeNull();
        statusResult!.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        var health = statusResult.Value as HealthDto;
        health.Should().NotBeNull();
        health!.Status.Should().Be("Unhealthy");
        health.Qdrant.Should().Be("Unhealthy");
    }
}
