namespace CellAssistant.UnitTests.Controllers;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Api.Controllers;
using CellAssistant.Application.DTOs;
using CellAssistant.Application.Options;
using CellAssistant.Application.Ports;
using CellAssistant.Domain.Entities;
using CellAssistant.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

public class DocumentsControllerAdversarialStressTests : IDisposable
{
    private readonly IDocumentRepository _repo = Substitute.For<IDocumentRepository>();
    private readonly ILogger<DocumentsController> _logger = Substitute.For<ILogger<DocumentsController>>();
    private readonly string _tempRoot;
    private readonly DocumentsController _controller;

    public DocumentsControllerAdversarialStressTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "DocCtrlSec_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Equipment"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Faults"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Manuals"));

        File.WriteAllText(Path.Combine(_tempRoot, "Faults", "Gripper.md"), "# Robot Gripper Details");
        File.WriteAllText(Path.Combine(_tempRoot, "Equipment", "ROBOT01.md"), "# ROBOT01 Specs");
        File.WriteAllText(Path.Combine(_tempRoot, "Manuals", "Robot.pdf"), "%PDF-1.4 Mock Robot PDF");

        var kbOptions = Options.Create(new KnowledgeBaseOptions
        {
            RootPath = _tempRoot,
            SqlitePath = Path.Combine(_tempRoot, "metadata.db")
        });

        _controller = new DocumentsController(_repo, kbOptions, _logger)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        _controller.HttpContext.Items["CorrelationId"] = "test-correlation-uuid-1234";
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    #region 1. Adversarial Path Query Parameter Payloads

    [Theory]
    [InlineData("../../../../../../../../../../../../etc/passwd")]
    [InlineData("../../../../../../../../../../../../etc/shadow")]
    [InlineData("..\\..\\..\\..\\..\\..\\..\\..\\..\\..\\..\\..\\etc\\passwd")]
    [InlineData("Equipment/../../../../../../../../../../../../etc/passwd")]
    [InlineData("Faults/Gripper.md/../../../../../../../../../../../../etc/shadow")]
    [InlineData("..%2f..%5c..%2fetc/passwd")]
    [InlineData("%252e%252e%252f%252e%252e%252fetc%252fpasswd")]
    [InlineData("%25252e%25252e%25252fetc%25252fpasswd")]
    [InlineData("Manuals/Robot.pdf\0/../../etc/passwd")]
    [InlineData("Manuals/Robot.pdf%00/../../etc/passwd")]
    [InlineData("Manuals/Robot.pdf%2500")]
    [InlineData("././././../secret")]
    [InlineData("/")]
    [InlineData(".")]
    [InlineData("Equipment/")]
    [InlineData("Faults")]
    [InlineData("Faults/")]
    public async Task GetDocumentFile_AdversarialPathQuery_Returns403Forbidden(string maliciousPath)
    {
        var result = await _controller.GetDocumentFileAsync("arbitrary-id", path: maliciousPath);

        var forbidden = result as ObjectResult;
        forbidden.Should().NotBeNull($"Expected ObjectResult for path '{maliciousPath}'");
        forbidden!.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        var err = forbidden.Value as ApiErrorDto;
        err.Should().NotBeNull();
        err!.CorrelationId.Should().Be("test-correlation-uuid-1234");
        err.Message.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region 2. Adversarial Route Parameter ID Payloads

    [Theory]
    [InlineData("../../../../../../../../../../../../etc/passwd")]
    [InlineData("..%2f..%5c..%2fetc/passwd")]
    [InlineData("%252e%252e%252fetc%252fpasswd")]
    [InlineData("Manuals%00Robot.pdf")]
    [InlineData("././././../secret")]
    [InlineData("Faults/")]
    [InlineData("Equipment")]
    public async Task GetDocumentFile_AdversarialRouteId_Returns403Forbidden(string maliciousId)
    {
        var result = await _controller.GetDocumentFileAsync(maliciousId, path: null);

        var forbidden = result as ObjectResult;
        forbidden.Should().NotBeNull($"Expected ObjectResult for ID '{maliciousId}'");
        forbidden!.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        var err = forbidden.Value as ApiErrorDto;
        err.Should().NotBeNull();
        err!.CorrelationId.Should().Be("test-correlation-uuid-1234");
    }

    #endregion

    #region 3. Malicious Document FilePath Stored in Repository

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("../../../../../etc/shadow")]
    [InlineData("../../secret.key")]
    [InlineData("Faults/Gripper.md\0.exe")]
    public async Task GetDocumentFile_MaliciousFilePathInRepo_Returns403Forbidden(string tamperedFilePath)
    {
        var docId = Guid.NewGuid();
        var doc = new Document
        {
            Id = docId,
            FileName = "Exploit.txt",
            FilePath = tamperedFilePath,
            RelativePath = tamperedFilePath,
            DocumentType = DocumentType.Text,
            Folder = "Faults",
            ContentHash = "hash-tampered",
            CreatedUtc = DateTimeOffset.UtcNow,
            ModifiedUtc = DateTimeOffset.UtcNow,
            IndexedUtc = DateTimeOffset.UtcNow
        };

        _repo.GetByIdAsync(docId, Arg.Any<CancellationToken>()).Returns(doc);

        var result = await _controller.GetDocumentFileAsync(docId.ToString(), path: null);

        var forbidden = result as ObjectResult;
        forbidden.Should().NotBeNull();
        forbidden!.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        var err = forbidden.Value as ApiErrorDto;
        err.Should().NotBeNull();
        err!.CorrelationId.Should().Be("test-correlation-uuid-1234");
    }

    #endregion

    #region 4. Valid Retrieval & Content-Disposition Integrity

    [Fact]
    public async Task GetDocumentFile_ValidMarkdown_ServesTextPlainWithInlineHeader()
    {
        var result = await _controller.GetDocumentFileAsync("non-guid", path: "Faults/Gripper.md");

        var fileResult = result as FileStreamResult;
        fileResult.Should().NotBeNull();
        fileResult!.ContentType.Should().Be("text/plain; charset=utf-8");
        fileResult.EnableRangeProcessing.Should().BeTrue();
        fileResult.FileStream.CanRead.Should().BeTrue();
        fileResult.FileStream.CanWrite.Should().BeFalse();

        _controller.Response.Headers.ContentDisposition.ToString().Should().Be("inline; filename=\"Gripper.md\"");
        fileResult.FileStream.Dispose();
    }

    [Fact]
    public async Task GetDocumentFile_ValidPdf_ServesApplicationPdf()
    {
        var result = await _controller.GetDocumentFileAsync("non-guid", path: "Manuals/Robot.pdf");

        var fileResult = result as FileStreamResult;
        fileResult.Should().NotBeNull();
        fileResult!.ContentType.Should().Be("application/pdf");
        fileResult.EnableRangeProcessing.Should().BeTrue();

        _controller.Response.Headers.ContentDisposition.ToString().Should().Be("inline; filename=\"Robot.pdf\"");
        fileResult.FileStream.Dispose();
    }

    [Fact]
    public async Task GetDocumentFile_SelfReferencingValidPath_ResolvesSafely()
    {
        var result = await _controller.GetDocumentFileAsync("non-guid", path: "./Faults/../Equipment/ROBOT01.md");

        var fileResult = result as FileStreamResult;
        fileResult.Should().NotBeNull();
        fileResult!.ContentType.Should().Be("text/plain; charset=utf-8");
        fileResult.FileStream.Dispose();
    }

    #endregion

    #region 5. Missing Files and Non-Existent Guids

    [Fact]
    public async Task GetDocumentFile_NonExistentFileOnDisk_Returns404NotFound()
    {
        var result = await _controller.GetDocumentFileAsync("non-guid", path: "Faults/MissingDoc.md");

        var notFound = result as ObjectResult;
        notFound.Should().NotBeNull();
        notFound!.StatusCode.Should().Be(StatusCodes.Status404NotFound);

        var err = notFound.Value as ApiErrorDto;
        err.Should().NotBeNull();
        err!.Message.Should().Contain("does not exist on disk");
    }

    [Fact]
    public async Task GetDocumentFile_UnknownGuid_Returns404NotFound()
    {
        var randomId = Guid.NewGuid();
        _repo.GetByIdAsync(randomId, Arg.Any<CancellationToken>()).Returns((Document?)null);

        var result = await _controller.GetDocumentFileAsync(randomId.ToString());

        var notFound = result as NotFoundObjectResult;
        notFound.Should().NotBeNull();
        notFound!.StatusCode.Should().Be(StatusCodes.Status404NotFound);

        var err = notFound.Value as ApiErrorDto;
        err.Should().NotBeNull();
        err!.Message.Should().Contain("was not found in the registry");
    }

    #endregion
}
