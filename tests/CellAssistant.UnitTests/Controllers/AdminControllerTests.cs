namespace CellAssistant.UnitTests.Controllers;

using System;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Api.Controllers;
using CellAssistant.Application.DTOs;
using CellAssistant.Application.Ports;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

public class AdminControllerTests
{
    private readonly IIndexerStateRepository _indexerRepo = Substitute.For<IIndexerStateRepository>();
    private readonly IServiceScopeFactory _scopeFactory = Substitute.For<IServiceScopeFactory>();
    private readonly ILogger<AdminController> _logger = Substitute.For<ILogger<AdminController>>();
    private readonly AdminController _controller;

    public AdminControllerTests()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        _scopeFactory.CreateScope().Returns(scope);

        _controller = new AdminController(_indexerRepo, _scopeFactory, _logger)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    [Fact]
    public async Task TriggerReindexAsync_Returns202AcceptedWithReindexResponseDto()
    {
        var result = await _controller.TriggerReindexAsync();

        var accepted = result as ObjectResult;
        accepted.Should().NotBeNull();
        accepted!.StatusCode.Should().Be(StatusCodes.Status202Accepted);

        var response = accepted.Value as ReindexResponseDto;
        response.Should().NotBeNull();
        response!.Status.Should().Contain("Reindex requested");
        response.ReindexRequested.Should().BeTrue();
        response.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));

        await _indexerRepo.Received(1).SetReindexRequestedAsync(true, Arg.Any<CancellationToken>());
    }
}
