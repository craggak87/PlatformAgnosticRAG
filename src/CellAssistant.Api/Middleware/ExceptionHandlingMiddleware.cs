namespace CellAssistant.Api.Middleware;

using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CellAssistant.Application.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string correlationId = context.Request.Headers["X-Correlation-ID"].ToString();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString();
        }

        context.Items["CorrelationId"] = correlationId;
        context.Response.Headers["X-Correlation-ID"] = correlationId;

        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogInformation("Request was cancelled by the client. CorrelationId: {CorrelationId}, Path: {Path}",
                correlationId, context.Request.Path);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex, correlationId);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception, string correlationId)
    {
        if (context.Response.HasStarted)
        {
            _logger.LogWarning("Response has already started; unable to write structured error. CorrelationId: {CorrelationId}",
                correlationId);
            return;
        }

        var (statusCode, clientMessage) = exception switch
        {
            BadHttpRequestException badReq => (StatusCodes.Status400BadRequest, badReq.Message),
            ArgumentException argEx => (StatusCodes.Status400BadRequest, argEx.Message),
            ValidationException valEx => (StatusCodes.Status400BadRequest, valEx.Message),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Access to the requested resource is forbidden."),
            FileNotFoundException => (StatusCodes.Status404NotFound, "The requested file or resource was not found."),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "The requested resource was not found."),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception,
                "Unhandled error processing HTTP {Method} {Path}. CorrelationId: {CorrelationId}",
                context.Request.Method, context.Request.Path, correlationId);
        }
        else
        {
            _logger.LogWarning(
                "Client error {StatusCode} processing HTTP {Method} {Path}: {Message}. CorrelationId: {CorrelationId}",
                statusCode, context.Request.Method, context.Request.Path, exception.Message, correlationId);
        }

        context.Response.Clear();
        context.Response.Headers["X-Correlation-ID"] = correlationId;
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        var errorResponse = new ApiErrorDto(
            message: clientMessage,
            correlationId: correlationId,
            error: clientMessage,
            timestamp: DateTimeOffset.UtcNow
        );

        string json = JsonSerializer.Serialize(errorResponse, JsonOptions);
        await context.Response.WriteAsync(json);
    }
}
