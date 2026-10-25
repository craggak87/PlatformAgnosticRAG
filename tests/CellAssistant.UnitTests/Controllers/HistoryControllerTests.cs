namespace CellAssistant.UnitTests.Controllers;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Api.Controllers;
using CellAssistant.Application.DTOs;
using CellAssistant.Application.Ports;
using CellAssistant.Domain.Entities;
using CellAssistant.Domain.Enums;
using CellAssistant.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

public class HistoryControllerTests
{
    private readonly ISearchHistoryRepository _historyRepo = Substitute.For<ISearchHistoryRepository>();
    private readonly HistoryController _controller;

    public HistoryControllerTests()
    {
        _controller = new HistoryController(_historyRepo)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    [Fact]
    public async Task GetRecentHistoryAsync_Returns200WithHistoryEntries()
    {
        var entries = new List<SearchHistoryEntry>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Question = "What causes gripper timeout?",
                Answer = "Cable damage on input I:0/15.",
                Confidence = ConfidenceLevel.High,
                Sources = new List<SourceReference>
                {
                    new(Guid.NewGuid(), "Gripper.md", "Faults/Gripper.md", "Faults", null, "Root Cause")
                },
                AskedUtc = DateTimeOffset.UtcNow
            }
        };

        _historyRepo.GetRecentAsync(20, Arg.Any<CancellationToken>()).Returns(entries);

        var result = await _controller.GetRecentHistoryAsync(20);

        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(StatusCodes.Status200OK);
        var dtos = okResult.Value as IReadOnlyList<SearchHistoryDto>;
        dtos.Should().NotBeNull();
        dtos!.Should().HaveCount(1);
        dtos![0].Question.Should().Be("What causes gripper timeout?");
        dtos[0].Confidence.Should().Be("High");
        dtos[0].Sources.Should().HaveCount(1);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-5, 20)]
    [InlineData(150, 100)]
    [InlineData(5, 5)]
    public async Task GetRecentHistoryAsync_ClampsLimitBetween1And100(int requestedLimit, int expectedClampedLimit)
    {
        _historyRepo.GetRecentAsync(expectedClampedLimit, Arg.Any<CancellationToken>())
            .Returns(new List<SearchHistoryEntry>());

        var result = await _controller.GetRecentHistoryAsync(requestedLimit);

        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        await _historyRepo.Received(1).GetRecentAsync(expectedClampedLimit, Arg.Any<CancellationToken>());
    }
}
