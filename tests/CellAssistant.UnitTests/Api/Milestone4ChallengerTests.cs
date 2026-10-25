namespace CellAssistant.UnitTests.Api;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Api.Middleware;
using CellAssistant.Application.DTOs;
using CellAssistant.Application.Ports;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

/// <summary>
/// Empirical Challenger test suite for Milestone 4:
/// 1. SSE streaming event sequence, formatting, and error emission.
/// 2. Exception handling middleware, correlation preservation, and zero path/stack-trace leakage.
/// 3. Static Web UI asset delivery, security headers, SPA fallback, and zero CDN references.
/// 4. Path traversal attack rejection and information disclosure prevention.
/// </summary>
public class Milestone4ChallengerTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _factory;

    public Milestone4ChallengerTests(ApiTestFixture factory)
    {
        _factory = factory;
    }

    // =========================================================================
    // 1. SSE Streaming Verification
    // =========================================================================

    [Fact]
    public async Task SSE_StreamQuery_SuccessSequence_EmitsEventsInStrictOrderAndFormat()
    {
        var client = _factory.CreateClient();
        var request = new QueryRequestDto("Explain ALM-205 causes", "CONV01", "Alarms");

        var metadata = new QueryMetadataDto(
            Confidence: "High",
            Sources: new List<SourceReferenceDto>
            {
                new(Guid.NewGuid(), "ALM-205 Safety Gate Open.md", "Alarms/ALM-205 Safety Gate Open.md", null, "Safety Interlock")
            },
            ConfidenceScore: 0.92,
            RetrievedSources: new List<SourceReferenceDto>()
        );

        _factory.MockQueryService.ExecuteQueryStreamAsync(
            Arg.Any<QueryRequestDto>(),
            Arg.Any<Func<string, Task>>(),
            Arg.Any<Func<QueryMetadataDto, Task>>(),
            Arg.Any<CancellationToken>())
            .Returns(async info =>
            {
                var onToken = info.ArgAt<Func<string, Task>>(1);
                var onMetadata = info.ArgAt<Func<QueryMetadataDto, Task>>(2);

                await onMetadata(metadata);
                await onToken("ALM-205 is triggered ");
                await onToken("when Safety Gate 2 ");
                await onToken("is opened.");
            });

        var response = await client.PostAsJsonAsync("/api/query/stream", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");
        response.Headers.CacheControl?.NoCache.Should().BeTrue();

        var body = await response.Content.ReadAsStringAsync();

        // 1. Verify strict event ordering: metadata -> token(s) -> done
        int metadataIndex = body.IndexOf("event: metadata\n");
        int token1Index = body.IndexOf("ALM-205 is triggered");
        int token2Index = body.IndexOf("when Safety Gate 2");
        int token3Index = body.IndexOf("is opened.");
        int doneIndex = body.IndexOf("event: done\n");

        metadataIndex.Should().BeGreaterThanOrEqualTo(0, "event: metadata must be emitted");
        token1Index.Should().BeGreaterThan(metadataIndex, "tokens must follow metadata");
        token2Index.Should().BeGreaterThan(token1Index, "token2 must follow token1");
        token3Index.Should().BeGreaterThan(token2Index, "token3 must follow token2");
        doneIndex.Should().BeGreaterThan(token3Index, "event: done must follow all tokens");

        // 2. Verify SSE line format: "event: <name>\ndata: <json>\n\n"
        body.Should().Contain("event: done\ndata: {}\n\n");

        // 3. Verify metadata payload is valid JSON and contains required properties
        string metadataLinePrefix = "event: metadata\ndata: ";
        int metaDataStart = body.IndexOf(metadataLinePrefix) + metadataLinePrefix.Length;
        int metaDataEnd = body.IndexOf("\n\n", metaDataStart);
        string metadataJson = body.Substring(metaDataStart, metaDataEnd - metaDataStart);

        using var metaDoc = JsonDocument.Parse(metadataJson);
        metaDoc.RootElement.GetProperty("confidence").GetString().Should().Be("High");
        metaDoc.RootElement.GetProperty("confidenceScore").GetDouble().Should().BeApproximately(0.92, 0.001);
        metaDoc.RootElement.GetProperty("sources").GetArrayLength().Should().Be(1);

        // 4. Verify token payload is valid JSON of form {"token":"..."}
        string tokenLinePrefix = "event: token\ndata: ";
        int tokenStart = body.IndexOf(tokenLinePrefix) + tokenLinePrefix.Length;
        int tokenEnd = body.IndexOf("\n\n", tokenStart);
        string tokenJson = body.Substring(tokenStart, tokenEnd - tokenStart);

        using var tokDoc = JsonDocument.Parse(tokenJson);
        tokDoc.RootElement.GetProperty("token").GetString().Should().Be("ALM-205 is triggered ");
    }

    [Fact]
    public async Task SSE_StreamQuery_ErrorBeforeTokens_EmitsErrorEventWithoutPathOrStackTrace()
    {
        var client = _factory.CreateClient();
        var request = new QueryRequestDto("Trigger internal error stream");

        _factory.MockQueryService.ExecuteQueryStreamAsync(
            Arg.Any<QueryRequestDto>(),
            Arg.Any<Func<string, Task>>(),
            Arg.Any<Func<QueryMetadataDto, Task>>(),
            Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("Fatal database failure in /var/db/secret/sqlite.db at line 777"));

        var response = await client.PostAsJsonAsync("/api/query/stream", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        var body = await response.Content.ReadAsStringAsync();

        // Must emit event: error
        body.Should().Contain("event: error\n");
        body.Should().NotContain("event: done\n");

        // Must not leak internal stack trace or paths
        body.Should().NotContain("/var/db/secret");
        body.Should().NotContain("line 777");
        body.Should().NotContain("InvalidOperationException");

        // Verify JSON payload format
        string errorPrefix = "event: error\ndata: ";
        int errStart = body.IndexOf(errorPrefix) + errorPrefix.Length;
        int errEnd = body.IndexOf("\n\n", errStart);
        string errorJson = body.Substring(errStart, errEnd - errStart);

        using var errDoc = JsonDocument.Parse(errorJson);
        errDoc.RootElement.GetProperty("message").GetString().Should().Be("An unexpected error occurred.");
        errDoc.RootElement.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task SSE_StreamQuery_ErrorMidStream_EmitsErrorEventFollowingEmittedTokens()
    {
        var client = _factory.CreateClient();
        var request = new QueryRequestDto("Midstream error test");

        _factory.MockQueryService.ExecuteQueryStreamAsync(
            Arg.Any<QueryRequestDto>(),
            Arg.Any<Func<string, Task>>(),
            Arg.Any<Func<QueryMetadataDto, Task>>(),
            Arg.Any<CancellationToken>())
            .Returns(async info =>
            {
                var onToken = info.ArgAt<Func<string, Task>>(1);
                var onMetadata = info.ArgAt<Func<QueryMetadataDto, Task>>(2);

                await onMetadata(new QueryMetadataDto("Medium", new List<SourceReferenceDto>()));
                await onToken("Partial content before crash. ");

                throw new ApplicationException("Unexpected crash during token streaming: /opt/internal/core.cs");
            });

        var response = await client.PostAsJsonAsync("/api/query/stream", request);
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("event: metadata\n");
        body.Should().Contain("Partial content before crash.");
        body.Should().Contain("event: error\n");
        body.Should().NotContain("event: done\n");

        // Zero information disclosure
        body.Should().NotContain("/opt/internal");
        body.Should().NotContain("ApplicationException");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SSE_StreamQuery_InvalidQuestion_Returns400BadRequestJson_NotSse(string invalidQuestion)
    {
        // When client sends invalid input to [Produces("text/event-stream", "application/json")] action returning BadRequest(ApiErrorDto),
        // ASP.NET Core content negotiation returns 400 Bad Request with JSON error payload.
        var client = _factory.CreateClient();
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/query/stream")
        {
            Content = JsonContent.Create(new QueryRequestDto(invalidQuestion))
        };
        requestMessage.Headers.Accept.Clear();
        requestMessage.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("*/*"));

        var response = await client.SendAsync(requestMessage);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var err = await response.Content.ReadFromJsonAsync<ApiErrorDto>();
        err.Should().NotBeNull();
        err!.CorrelationId.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SSE_StreamQuery_InvalidQuestion_WithExplicitJsonAccept_Returns400BadRequest(string invalidQuestion)
    {
        // When client explicitly specifies Accept: application/json on a streaming endpoint supporting application/json,
        // ASP.NET Core returns 400 Bad Request with ApiErrorDto.
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/query/stream", new QueryRequestDto(invalidQuestion));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var err = await response.Content.ReadFromJsonAsync<ApiErrorDto>();
        err.Should().NotBeNull();
        err!.CorrelationId.Should().NotBeNullOrWhiteSpace();
    }

    // =========================================================================
    // 2. Exception Handling Middleware & Path Leakage Testing
    // =========================================================================

    [Fact]
    public async Task Middleware_SystemException_Returns500WithZeroInformationDisclosure()
    {
        var logger = Substitute.For<ILogger<ExceptionHandlingMiddleware>>();
        RequestDelegate next = ctx => throw new DirectoryNotFoundException(
            "Could not find directory /home/alex-huza/secret-vault/keys/ at line 42 in VaultManager.cs");

        var middleware = new ExceptionHandlingMiddleware(next, logger);
        var context = new DefaultHttpContext();
        var bodyStream = new MemoryStream();
        context.Response.Body = bodyStream;

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        context.Response.ContentType.Should().Contain("application/json");

        bodyStream.Seek(0, SeekOrigin.Begin);
        var jsonText = await new StreamReader(bodyStream).ReadToEndAsync();

        // 1. Strict leakage checks: NO absolute path, NO file names, NO line numbers, NO exception class
        jsonText.Should().NotContain("/home/");
        jsonText.Should().NotContain("secret-vault");
        jsonText.Should().NotContain("VaultManager.cs");
        jsonText.Should().NotContain("line 42");
        jsonText.Should().NotContain("DirectoryNotFoundException");

        // 2. DTO structural integrity
        var error = JsonSerializer.Deserialize<ApiErrorDto>(jsonText, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        error.Should().NotBeNull();
        error!.Message.Should().Be("An unexpected error occurred.");
        error.Error.Should().Be("An unexpected error occurred.");
        error.CorrelationId.Should().NotBeNullOrWhiteSpace();
        error.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Middleware_CustomCorrelationId_MatchesAcrossHeaderAndBody()
    {
        const string customCorrId = "empirical-test-corr-987654";
        var logger = Substitute.For<ILogger<ExceptionHandlingMiddleware>>();
        RequestDelegate next = ctx => throw new InvalidOperationException("Something went wrong");

        var middleware = new ExceptionHandlingMiddleware(next, logger);
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-ID"] = customCorrId;
        var bodyStream = new MemoryStream();
        context.Response.Body = bodyStream;

        await middleware.InvokeAsync(context);

        // Verify response header retains custom correlation ID
        context.Response.Headers["X-Correlation-ID"].ToString().Should().Be(customCorrId);

        // Verify body correlation ID matches request header
        bodyStream.Seek(0, SeekOrigin.Begin);
        var jsonText = await new StreamReader(bodyStream).ReadToEndAsync();
        var error = JsonSerializer.Deserialize<ApiErrorDto>(jsonText, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        error.Should().NotBeNull();
        error!.CorrelationId.Should().Be(customCorrId);
    }

    [Fact]
    public async Task Middleware_UnauthorizedAccess_Returns403WithGenericMessage()
    {
        var logger = Substitute.For<ILogger<ExceptionHandlingMiddleware>>();
        RequestDelegate next = ctx => throw new UnauthorizedAccessException("Forbidden access to C:\\Windows\\System32\\config\\SAM");

        var middleware = new ExceptionHandlingMiddleware(next, logger);
        var context = new DefaultHttpContext();
        var bodyStream = new MemoryStream();
        context.Response.Body = bodyStream;

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        bodyStream.Seek(0, SeekOrigin.Begin);
        var jsonText = await new StreamReader(bodyStream).ReadToEndAsync();
        jsonText.Should().NotContain("System32");
        jsonText.Should().NotContain("SAM");

        var error = JsonSerializer.Deserialize<ApiErrorDto>(jsonText, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        error!.Message.Should().Be("Access to the requested resource is forbidden.");
    }

    [Fact]
    public async Task Middleware_FileNotFound_Returns404WithGenericMessage()
    {
        var logger = Substitute.For<ILogger<ExceptionHandlingMiddleware>>();
        RequestDelegate next = ctx => throw new FileNotFoundException("File not found: /etc/shadow.bak");

        var middleware = new ExceptionHandlingMiddleware(next, logger);
        var context = new DefaultHttpContext();
        var bodyStream = new MemoryStream();
        context.Response.Body = bodyStream;

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);

        bodyStream.Seek(0, SeekOrigin.Begin);
        var jsonText = await new StreamReader(bodyStream).ReadToEndAsync();
        jsonText.Should().NotContain("/etc/shadow");

        var error = JsonSerializer.Deserialize<ApiErrorDto>(jsonText, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        error!.Message.Should().Be("The requested file or resource was not found.");
    }

    // =========================================================================
    // 3. Static Web UI Assets & Air-Gapped Security Headers
    // =========================================================================

    [Fact]
    public async Task WebUI_IndexHtml_LoadsWithSecurityHeadersAndCsp()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");

        // Verify required security headers
        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Contain("SAMEORIGIN");
        response.Headers.GetValues("Content-Security-Policy").Should().ContainMatch("*default-src 'self'*");

        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("<title>Cell Knowledge Assistant");
        html.Should().Contain("href=\"style.css\"");
        html.Should().Contain("src=\"app.js\"");
    }

    [Fact]
    public async Task WebUI_StyleCss_ServedWithCorrectContentTypeAndSecurityHeaders()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/style.css");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/css");

        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Contain("SAMEORIGIN");

        var css = await response.Content.ReadAsStringAsync();
        css.Should().Contain("--bg-app: #0b0f17;");
        css.Should().Contain(".badge");
    }

    [Fact]
    public async Task WebUI_AppJs_ServedWithCorrectContentTypeAndSecurityHeaders()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/app.js");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Contain("javascript");

        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");

        var js = await response.Content.ReadAsStringAsync();
        js.Should().Contain("const App =");
        js.Should().Contain("App.init();");
        js.Should().Contain("fetch('/api/query/stream'");
    }

    [Fact]
    public async Task WebUI_SpaFallback_UnknownRoutesReturnIndexHtml()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/chat/conversation/session-abc-123");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");

        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("<title>Cell Knowledge Assistant");
    }

    [Fact]
    public void WebUI_AllStaticAssets_HaveZeroExternalCdnOrHttpReferences()
    {
        string wwwroot = Path.Combine(AppContext.BaseDirectory, "../../../../../src/CellAssistant.Api/wwwroot");
        if (!Directory.Exists(wwwroot))
        {
            wwwroot = Path.GetFullPath("src/CellAssistant.Api/wwwroot");
        }

        Directory.Exists(wwwroot).Should().BeTrue($"wwwroot directory must exist at '{wwwroot}'");

        var files = Directory.GetFiles(wwwroot, "*.*", SearchOption.AllDirectories);
        files.Should().NotBeEmpty();

        foreach (var file in files)
        {
            var content = File.ReadAllText(file);

            // Must NOT contain external web links or CDNs
            content.Should().NotContain("http://", $"File {Path.GetFileName(file)} must not reference external http://");
            content.Should().NotContain("https://", $"File {Path.GetFileName(file)} must not reference external https://");
            content.Should().NotContain("cdn.", $"File {Path.GetFileName(file)} must not reference any CDN");
            content.Should().NotContain("unpkg.com", $"File {Path.GetFileName(file)} must not reference unpkg");
            content.Should().NotContain("cdnjs.", $"File {Path.GetFileName(file)} must not reference cdnjs");
            content.Should().NotContain("jsdelivr.", $"File {Path.GetFileName(file)} must not reference jsdelivr");
            content.Should().NotContain("googleapis.com", $"File {Path.GetFileName(file)} must not reference Google APIs/fonts");
        }
    }

    // =========================================================================
    // 4. Path Traversal & File Endpoint Hardening
    // =========================================================================

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\windows\\system32\\drivers\\etc\\hosts")]
    [InlineData("%2e%2e%2f%2e%2e%2fetc%2fpasswd")]
    [InlineData("%252e%252e%252fetc%252fpasswd")]
    [InlineData("Faults/../../etc/shadow")]
    [InlineData("Faults%2f..%2f..%2fetc%2fshadow")]
    [InlineData("test\0file.md")]
    [InlineData("test%00file.md")]
    public async Task DocumentsEndpoint_AdversarialPathTraversal_RejectsWithoutInternalLeakage(string attackPath)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/documents/any-id/file?path={attackPath}");

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Forbidden,
            HttpStatusCode.BadRequest,
            HttpStatusCode.NotFound);

        var body = await response.Content.ReadAsStringAsync();

        // Must NOT leak internal absolute server paths
        body.Should().NotContain("/home/");
        body.Should().NotContain("PlatformAgnosticRAG");
        body.Should().NotContain("sample-data");

        // Verify response is valid structured error
        var error = JsonSerializer.Deserialize<ApiErrorDto>(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        error.Should().NotBeNull();
        error!.CorrelationId.Should().NotBeNullOrWhiteSpace();
    }
}
