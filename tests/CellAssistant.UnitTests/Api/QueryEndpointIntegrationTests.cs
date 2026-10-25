namespace CellAssistant.UnitTests.Api;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Application.DTOs;
using FluentAssertions;
using NSubstitute;
using Xunit;

public class QueryEndpointIntegrationTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _factory;

    public QueryEndpointIntegrationTests(ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostQuery_ValidRequest_Returns200Ok()
    {
        var client = _factory.CreateClient();
        var request = new QueryRequestDto("What causes gripper timeout?", "ROBOT01", "Faults");
        var expectedResponse = new QueryResponseDto(
            "Gripper timeout is caused by cable damage on input I:0/15.",
            "High",
            new List<SourceReferenceDto>(),
            ConfidenceScore: 0.88,
            RetrievedSources: new List<SourceReferenceDto>(),
            ExecutionTimeMs: 45
        );

        _factory.MockQueryService.ExecuteQueryAsync(Arg.Any<QueryRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(expectedResponse);

        var response = await client.PostAsJsonAsync("/api/query", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<QueryResponseDto>();
        body.Should().NotBeNull();
        body!.Answer.Should().Contain("I:0/15");
        body.Confidence.Should().Be("High");
    }

    [Fact]
    public async Task PostQuery_QueryAliasProperty_BindsAndSucceeds()
    {
        var client = _factory.CreateClient();
        var jsonContent = new StringContent(
            "{\"query\":\"What causes alarm ALM-205?\",\"equipmentId\":\"CONV01\",\"folder\":\"Alarms\",\"topK\":3}",
            System.Text.Encoding.UTF8,
            "application/json");

        var expectedResponse = new QueryResponseDto(
            "ALM-205 indicates safety gate interlock open.",
            "High",
            new List<SourceReferenceDto>()
        );

        _factory.MockQueryService.ExecuteQueryAsync(Arg.Any<QueryRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(expectedResponse);

        var response = await client.PostAsync("/api/query", jsonContent);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<QueryResponseDto>();
        body.Should().NotBeNull();
        body!.Answer.Should().Contain("ALM-205");
    }

    [Fact]
    public async Task PostQuery_EmptyQuestion_Returns400BadRequest()
    {
        var client = _factory.CreateClient();
        var request = new QueryRequestDto("");

        var response = await client.PostAsJsonAsync("/api/query", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var err = await response.Content.ReadFromJsonAsync<ApiErrorDto>();
        err.Should().NotBeNull();
        err!.CorrelationId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task PostQuery_QuestionTooLong_Returns400BadRequest()
    {
        var client = _factory.CreateClient();
        var longQuestion = new string('A', 2001);
        var request = new QueryRequestDto(longQuestion);

        var response = await client.PostAsJsonAsync("/api/query", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var err = await response.Content.ReadFromJsonAsync<ApiErrorDto>();
        err.Should().NotBeNull();
        err!.Message.Should().Contain("2000");
    }

    [Fact]
    public async Task PostQueryStream_ValidRequest_EmitsTextEventStream()
    {
        var client = _factory.CreateClient();
        var request = new QueryRequestDto("Stream test question");

        _factory.MockQueryService.ExecuteQueryStreamAsync(
            Arg.Any<QueryRequestDto>(),
            Arg.Any<Func<string, Task>>(),
            Arg.Any<Func<QueryMetadataDto, Task>>(),
            Arg.Any<CancellationToken>())
            .Returns(async info =>
            {
                var onToken = info.ArgAt<Func<string, Task>>(1);
                var onMetadata = info.ArgAt<Func<QueryMetadataDto, Task>>(2);

                await onMetadata(new QueryMetadataDto("High", new List<SourceReferenceDto>()));
                await onToken("Token1 ");
                await onToken("Token2");
            });

        var response = await client.PostAsJsonAsync("/api/query/stream", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        var text = await response.Content.ReadAsStringAsync();
        text.Should().Contain("event: metadata\n");
        text.Should().Contain("event: token\n");
        text.Should().Contain("Token1 ");
        text.Should().Contain("Token2");
        text.Should().Contain("event: done\n");
    }
}
