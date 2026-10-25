namespace CellAssistant.UnitTests.Controllers;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Api.Controllers;
using CellAssistant.Application.DTOs;
using CellAssistant.Application.Ports;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

public class QueryControllerTests
{
    private readonly IQueryService _queryService = Substitute.For<IQueryService>();
    private readonly ILogger<QueryController> _logger = Substitute.For<ILogger<QueryController>>();
    private readonly QueryController _controller;

    public QueryControllerTests()
    {
        _controller = new QueryController(_queryService, _logger)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    [Fact]
    public async Task QueryAsync_ValidRequest_Returns200WithQueryResponseDto()
    {
        var request = new QueryRequestDto("What causes gripper timeout?", "ROBOT01", "Faults");
        var expectedResponse = new QueryResponseDto(
            "Gripper timeout is caused by cable damage.",
            "High",
            new List<SourceReferenceDto>(),
            ConfidenceScore: 0.88,
            RetrievedSources: new List<SourceReferenceDto>(),
            ExecutionTimeMs: 42
        );

        _queryService.ExecuteQueryAsync(request, Arg.Any<CancellationToken>())
            .Returns(expectedResponse);

        var result = await _controller.QueryAsync(request);

        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(StatusCodes.Status200OK);
        okResult.Value.Should().BeEquivalentTo(expectedResponse);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task QueryAsync_EmptyQuestion_Returns400BadRequest(string? question)
    {
        var request = new QueryRequestDto { Question = question! };

        var result = await _controller.QueryAsync(request);

        var badRequest = result as BadRequestObjectResult;
        badRequest.Should().NotBeNull();
        badRequest!.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var err = badRequest.Value as ApiErrorDto;
        err.Should().NotBeNull();
        err!.Message.Should().Contain("required and cannot be empty");
    }

    [Fact]
    public async Task QueryAsync_QuestionTooLong_Returns400BadRequest()
    {
        var longQuestion = new string('A', 2001);
        var request = new QueryRequestDto(longQuestion);

        var result = await _controller.QueryAsync(request);

        var badRequest = result as BadRequestObjectResult;
        badRequest.Should().NotBeNull();
        badRequest!.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var err = badRequest.Value as ApiErrorDto;
        err.Should().NotBeNull();
        err!.Message.Should().Contain("cannot exceed 2000 characters");
    }

    [Fact]
    public async Task StreamQueryAsync_ValidRequest_StreamsSseEvents()
    {
        var request = new QueryRequestDto("What causes alarm ALM-205?");
        var responseBody = new MemoryStream();
        _controller.HttpContext.Response.Body = responseBody;

        _queryService.ExecuteQueryStreamAsync(
            request,
            Arg.Any<Func<string, Task>>(),
            Arg.Any<Func<QueryMetadataDto, Task>>(),
            Arg.Any<CancellationToken>())
            .Returns(async info =>
            {
                var onToken = info.ArgAt<Func<string, Task>>(1);
                var onMetadata = info.ArgAt<Func<QueryMetadataDto, Task>>(2);

                await onMetadata(new QueryMetadataDto("High", new List<SourceReferenceDto>(), 0.90, new List<SourceReferenceDto>()));
                await onToken("Safety gate ");
                await onToken("open loop.");
            });

        var result = await _controller.StreamQueryAsync(request);

        result.Should().BeOfType<EmptyResult>();
        _controller.HttpContext.Response.ContentType.Should().Contain("text/event-stream");

        responseBody.Seek(0, SeekOrigin.Begin);
        var sseText = await new StreamReader(responseBody).ReadToEndAsync();

        sseText.Should().Contain("event: metadata\n");
        sseText.Should().Contain("event: token\n");
        sseText.Should().Contain("Safety gate ");
        sseText.Should().Contain("open loop.");
        sseText.Should().Contain("event: done\n");
    }

    [Fact]
    public async Task StreamQueryAsync_EmptyQuestion_Returns400BadRequest()
    {
        var request = new QueryRequestDto("");

        var result = await _controller.StreamQueryAsync(request);

        var badRequest = result as BadRequestObjectResult;
        badRequest.Should().NotBeNull();
        badRequest!.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }
}
