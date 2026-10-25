namespace CellAssistant.UnitTests.Middleware;

using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CellAssistant.Api.Middleware;
using CellAssistant.Application.DTOs;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

public class ExceptionHandlingMiddlewareTests
{
    private readonly ILogger<ExceptionHandlingMiddleware> _logger = Substitute.For<ILogger<ExceptionHandlingMiddleware>>();

    [Fact]
    public async Task InvokeAsync_WhenNoException_CallsNextMiddleware()
    {
        bool nextCalled = false;
        RequestDelegate next = ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new ExceptionHandlingMiddleware(next, _logger);
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.Headers.Should().ContainKey("X-Correlation-ID");
    }

    [Fact]
    public async Task InvokeAsync_ExistingCorrelationIdHeader_IsPreserved()
    {
        const string existingCorrelation = "custom-corr-12345";
        RequestDelegate next = ctx => Task.CompletedTask;

        var middleware = new ExceptionHandlingMiddleware(next, _logger);
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-ID"] = existingCorrelation;

        await middleware.InvokeAsync(context);

        context.Response.Headers["X-Correlation-ID"].ToString().Should().Be(existingCorrelation);
        context.Items["CorrelationId"].Should().Be(existingCorrelation);
    }

    [Fact]
    public async Task InvokeAsync_UnhandledException_Returns500AndStructuredJsonWithoutStackTrace()
    {
        RequestDelegate next = ctx => throw new InvalidOperationException("Internal secret db connection failed: Server=10.0.0.1;Uid=admin;Pwd=secret; at CellAssistant.Core()");

        var middleware = new ExceptionHandlingMiddleware(next, _logger);
        var context = new DefaultHttpContext();
        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        context.Response.ContentType.Should().Contain("application/json");

        responseBody.Seek(0, SeekOrigin.Begin);
        var responseText = await new StreamReader(responseBody).ReadToEndAsync();

        responseText.Should().NotContain("at CellAssistant.");
        responseText.Should().NotContain("System.Exception");
        responseText.Should().NotContain("secret db connection");
        responseText.Should().NotContain("10.0.0.1");

        var error = JsonSerializer.Deserialize<ApiErrorDto>(responseText, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        error.Should().NotBeNull();
        error!.Message.Should().Be("An unexpected error occurred.");
        error.CorrelationId.Should().NotBeNullOrWhiteSpace();
        error.Error.Should().Be("An unexpected error occurred.");
    }

    [Fact]
    public async Task InvokeAsync_ArgumentException_Returns400()
    {
        RequestDelegate next = ctx => throw new ArgumentException("Invalid query parameter value.");

        var middleware = new ExceptionHandlingMiddleware(next, _logger);
        var context = new DefaultHttpContext();
        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        responseBody.Seek(0, SeekOrigin.Begin);
        var responseText = await new StreamReader(responseBody).ReadToEndAsync();
        responseText.Should().Contain("Invalid query parameter value.");
    }

    [Fact]
    public async Task InvokeAsync_ValidationException_Returns400()
    {
        RequestDelegate next = ctx => throw new ValidationException("Field validation failed.");

        var middleware = new ExceptionHandlingMiddleware(next, _logger);
        var context = new DefaultHttpContext();
        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        responseBody.Seek(0, SeekOrigin.Begin);
        var responseText = await new StreamReader(responseBody).ReadToEndAsync();
        responseText.Should().Contain("Field validation failed.");
    }

    [Fact]
    public async Task InvokeAsync_UnauthorizedAccessException_Returns403()
    {
        RequestDelegate next = ctx => throw new UnauthorizedAccessException("Forbidden access.");

        var middleware = new ExceptionHandlingMiddleware(next, _logger);
        var context = new DefaultHttpContext();
        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        responseBody.Seek(0, SeekOrigin.Begin);
        var responseText = await new StreamReader(responseBody).ReadToEndAsync();
        responseText.Should().Contain("Access to the requested resource is forbidden.");
    }

    [Fact]
    public async Task InvokeAsync_FileNotFoundException_Returns404()
    {
        RequestDelegate next = ctx => throw new FileNotFoundException("Missing file.");

        var middleware = new ExceptionHandlingMiddleware(next, _logger);
        var context = new DefaultHttpContext();
        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);

        responseBody.Seek(0, SeekOrigin.Begin);
        var responseText = await new StreamReader(responseBody).ReadToEndAsync();
        responseText.Should().Contain("The requested file or resource was not found.");
    }
}
