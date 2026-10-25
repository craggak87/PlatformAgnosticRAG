namespace CellAssistant.Api.Controllers;

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Application.DTOs;
using CellAssistant.Application.Ports;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

[ApiController]
[Route("api/query")]
[Produces("application/json")]
public class QueryController : ControllerBase
{
    private readonly IQueryService _queryService;
    private readonly ILogger<QueryController> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public QueryController(
        IQueryService queryService,
        ILogger<QueryController> logger)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes a synchronous natural language industrial engineering query.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(QueryResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> QueryAsync(
        [FromBody] QueryRequestDto request,
        CancellationToken cancellationToken = default)
    {
        string correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();

        if (request == null || string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest(new ApiErrorDto("Question is required and cannot be empty.", correlationId));
        }

        if (request.Question.Length > 2000)
        {
            return BadRequest(new ApiErrorDto("Question cannot exceed 2000 characters.", correlationId));
        }

        _logger.LogInformation("Executing query for '{Question}'. Correlation: {CorrelationId}",
            request.Question, correlationId);

        var response = await _queryService.ExecuteQueryAsync(request, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Executes a streaming query emitting metadata, token, done, or error SSE events.
    /// </summary>
    [HttpPost("stream")]
    [Produces("text/event-stream", "application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> StreamQueryAsync(
        [FromBody] QueryRequestDto request,
        CancellationToken cancellationToken = default)
    {
        string correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();

        if (request == null || string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest(new ApiErrorDto("Question is required and cannot be empty.", correlationId));
        }

        if (request.Question.Length > 2000)
        {
            return BadRequest(new ApiErrorDto("Question cannot exceed 2000 characters.", correlationId));
        }

        Response.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers.CacheControl = "no-cache, no-transform";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, HttpContext.RequestAborted);

        try
        {
            await _queryService.ExecuteQueryStreamAsync(
                request,
                async token =>
                {
                    var json = JsonSerializer.Serialize(new { token }, JsonOptions);
                    await Response.WriteAsync($"event: token\ndata: {json}\n\n", linkedCts.Token);
                    await Response.Body.FlushAsync(linkedCts.Token);
                },
                async metadata =>
                {
                    var json = JsonSerializer.Serialize(metadata, JsonOptions);
                    await Response.WriteAsync($"event: metadata\ndata: {json}\n\n", linkedCts.Token);
                    await Response.Body.FlushAsync(linkedCts.Token);
                },
                linkedCts.Token);

            await Response.WriteAsync("event: done\ndata: {}\n\n", linkedCts.Token);
            await Response.Body.FlushAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogInformation("Streaming query was cancelled by client. CorrelationId: {CorrelationId}", correlationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Streaming error occurred. CorrelationId: {CorrelationId}", correlationId);
            var errJson = JsonSerializer.Serialize(new
            {
                message = "An unexpected error occurred.",
                correlationId = correlationId
            }, JsonOptions);

            try
            {
                await Response.WriteAsync($"event: error\ndata: {errJson}\n\n", CancellationToken.None);
                await Response.Body.FlushAsync(CancellationToken.None);
            }
            catch
            {
                // Client connection terminated
            }
        }

        return new EmptyResult();
    }
}
