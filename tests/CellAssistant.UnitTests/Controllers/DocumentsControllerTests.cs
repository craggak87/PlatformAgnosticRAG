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

public class DocumentsControllerTests : IDisposable
{
    private readonly IDocumentRepository _repo = Substitute.For<IDocumentRepository>();
    private readonly ILogger<DocumentsController> _logger = Substitute.For<ILogger<DocumentsController>>();
    private readonly string _tempRoot;
    private readonly DocumentsController _controller;

    public DocumentsControllerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "KbRoot_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Faults"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Manuals"));

        File.WriteAllText(Path.Combine(_tempRoot, "Faults", "Gripper.md"), "# Robot Gripper");
        File.WriteAllText(Path.Combine(_tempRoot, "Manuals", "Robot.pdf"), "%PDF-1.4 Mock");

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
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GetDocumentFile_ValidGuid_ReturnsFileStreamResult()
    {
        var docId = Guid.NewGuid();
        var doc = new Document
        {
            Id = docId,
            FileName = "Gripper.md",
            FilePath = Path.Combine(_tempRoot, "Faults", "Gripper.md"),
            RelativePath = "Faults/Gripper.md",
            DocumentType = DocumentType.Markdown,
            Folder = "Faults",
            ContentHash = "hash123",
            CreatedUtc = DateTimeOffset.UtcNow,
            ModifiedUtc = DateTimeOffset.UtcNow,
            IndexedUtc = DateTimeOffset.UtcNow
        };

        _repo.GetByIdAsync(docId, Arg.Any<CancellationToken>()).Returns(doc);

        var result = await _controller.GetDocumentFileAsync(docId.ToString());

        var fileResult = result as FileStreamResult;
        fileResult.Should().NotBeNull();
        fileResult!.ContentType.Should().Be("text/plain; charset=utf-8");
        fileResult.EnableRangeProcessing.Should().BeTrue();
        fileResult.FileStream.CanRead.Should().BeTrue();
        fileResult.FileStream.CanWrite.Should().BeFalse();
        fileResult.FileStream.Dispose();
    }

    [Fact]
    public async Task GetDocumentFile_PdfFile_ReturnsApplicationPdf()
    {
        var docId = Guid.NewGuid();
        var doc = new Document
        {
            Id = docId,
            FileName = "Robot.pdf",
            FilePath = Path.Combine(_tempRoot, "Manuals", "Robot.pdf"),
            RelativePath = "Manuals/Robot.pdf",
            DocumentType = DocumentType.Pdf,
            Folder = "Manuals",
            ContentHash = "hash456",
            CreatedUtc = DateTimeOffset.UtcNow,
            ModifiedUtc = DateTimeOffset.UtcNow,
            IndexedUtc = DateTimeOffset.UtcNow
        };

        _repo.GetByIdAsync(docId, Arg.Any<CancellationToken>()).Returns(doc);

        var result = await _controller.GetDocumentFileAsync(docId.ToString());

        var fileResult = result as FileStreamResult;
        fileResult.Should().NotBeNull();
        fileResult!.ContentType.Should().Be("application/pdf");
        fileResult.FileStream.Dispose();
    }

    [Fact]
    public async Task GetDocumentFile_MissingGuid_Returns404NotFound()
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

    [Fact]
    public async Task GetDocumentFile_PathTraversalAttempt_Returns403Forbidden()
    {
        var result = await _controller.GetDocumentFileAsync("non-guid", path: "../../etc/passwd");

        var forbidden = result as ObjectResult;
        forbidden.Should().NotBeNull();
        forbidden!.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        var err = forbidden.Value as ApiErrorDto;
        err.Should().NotBeNull();
        err!.Message.Should().Contain("escapes the vault boundary");
    }

    [Fact]
    public async Task GetDocumentFile_MissingFileOnDisk_Returns404NotFound()
    {
        var result = await _controller.GetDocumentFileAsync("non-guid", path: "Faults/NonExistent.md");

        var notFound = result as ObjectResult;
        notFound.Should().NotBeNull();
        notFound!.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        var err = notFound.Value as ApiErrorDto;
        err.Should().NotBeNull();
        err!.Message.Should().Contain("does not exist on disk");
    }
}
