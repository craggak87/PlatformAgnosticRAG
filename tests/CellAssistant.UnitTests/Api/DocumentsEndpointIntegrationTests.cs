namespace CellAssistant.UnitTests.Api;

using System;
using System.IO;
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

public class DocumentsEndpointIntegrationTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _factory;

    public DocumentsEndpointIntegrationTests(ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("../../../../../../../../../../../../etc/passwd")]
    [InlineData("..\\..\\..\\..\\..\\..\\..\\..\\..\\..\\..\\..\\etc\\passwd")]
    [InlineData("Equipment/../../../../../../../../../../../../etc/passwd")]
    [InlineData("..%2f..%5c..%2fetc/passwd")]
    [InlineData("%252e%252e%252f%252e%252e%252fetc%252fpasswd")]
    [InlineData("%25252e%25252e%25252fetc%25252fpasswd")]
    [InlineData("Manuals/Robot.pdf\0/../../etc/passwd")]
    [InlineData("Manuals/Robot.pdf%00/../../etc/passwd")]
    [InlineData("Manuals/Robot.pdf%2500")]
    [InlineData("././././../secret")]
    [InlineData("/")]
    [InlineData(".")]
    public async Task GetDocumentFile_PathTraversalQueryParameter_Returns403Forbidden(string attackPayload)
    {
        var client = _factory.CreateClient();

        // Pass payload in the path query parameter
        string url = $"/api/documents/probe-id/file?path={Uri.EscapeDataString(attackPayload)}";
        var response = await client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            $"Adversarial payload '{attackPayload}' must result in HTTP 403 Forbidden");

        response.Headers.Contains("X-Correlation-ID").Should().BeTrue();

        var err = await response.Content.ReadFromJsonAsync<ApiErrorDto>();
        err.Should().NotBeNull();
        err!.CorrelationId.Should().NotBeNullOrWhiteSpace();
        err.Message.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetDocumentFile_MaliciousFilePathStoredInDatabase_Returns403Forbidden()
    {
        var client = _factory.CreateClient();
        var docId = Guid.NewGuid();

        var maliciousDoc = new Document
        {
            Id = docId,
            FileName = "passwd",
            FilePath = "/etc/passwd",
            RelativePath = "/etc/passwd",
            DocumentType = DocumentType.Text,
            Folder = "System",
            ContentHash = "exploit-hash",
            CreatedUtc = DateTimeOffset.UtcNow,
            ModifiedUtc = DateTimeOffset.UtcNow,
            IndexedUtc = DateTimeOffset.UtcNow
        };

        _factory.MockDocumentRepository.GetByIdAsync(docId, Arg.Any<CancellationToken>())
            .Returns(maliciousDoc);

        var response = await client.GetAsync($"/api/documents/{docId}/file");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "Document with external FilePath in repository must be blocked by PathSecurityValidator");

        var err = await response.Content.ReadFromJsonAsync<ApiErrorDto>();
        err.Should().NotBeNull();
        err!.Message.Should().Contain("escapes the vault boundary");
    }

    [Fact]
    public async Task GetDocumentFile_UnknownDocumentGuid_Returns404NotFound()
    {
        var client = _factory.CreateClient();
        var randomId = Guid.NewGuid();

        _factory.MockDocumentRepository.GetByIdAsync(randomId, Arg.Any<CancellationToken>())
            .Returns((Document?)null);

        var response = await client.GetAsync($"/api/documents/{randomId}/file");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var err = await response.Content.ReadFromJsonAsync<ApiErrorDto>();
        err.Should().NotBeNull();
        err!.Message.Should().Contain("was not found in the registry");
    }
}
