namespace CellAssistant.Api.Controllers;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Api.Security;
using CellAssistant.Application.DTOs;
using CellAssistant.Application.Options;
using CellAssistant.Application.Ports;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

[ApiController]
[Route("api/documents")]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentRepository _documentRepository;
    private readonly KnowledgeBaseOptions _kbOptions;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        IDocumentRepository documentRepository,
        IOptions<KnowledgeBaseOptions> kbOptions,
        ILogger<DocumentsController> logger)
    {
        _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
        _kbOptions = kbOptions?.Value ?? throw new ArgumentNullException(nameof(kbOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Serves a raw binary or text file for document inspection.
    /// Supports both Document GUID lookup and relative path lookup.
    /// </summary>
    /// <param name="id">Document GUID or relative file path inside the knowledge base.</param>
    /// <param name="path">Optional relative path override.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("{id}/file")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDocumentFileAsync(
        [FromRoute] string id,
        [FromQuery] string? path = null,
        CancellationToken cancellationToken = default)
    {
        string correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();

        string candidatePath;

        // 1. If explicit query parameter path is provided, prioritize it
        if (!string.IsNullOrWhiteSpace(path))
        {
            candidatePath = path;
        }
        // 2. Otherwise inspect {id}
        else if (Guid.TryParse(id, out var docGuid))
        {
            var document = await _documentRepository.GetByIdAsync(docGuid, cancellationToken);
            if (document == null)
            {
                _logger.LogWarning("Document ID '{DocumentId}' not found in SQLite registry. Correlation: {CorrelationId}",
                    docGuid, correlationId);
                return NotFound(new ApiErrorDto($"Document with ID '{id}' was not found in the registry.", correlationId));
            }

            candidatePath = document.FilePath;
        }
        else
        {
            // id is treated as a relative path
            candidatePath = id;
        }

        // 3. Strict Canonical Path Traversal Validation
        var validation = PathSecurityValidator.ValidateAndResolve(_kbOptions.RootPath, candidatePath);
        if (!validation.IsSuccess)
        {
            _logger.LogWarning("Path validation failed for '{Candidate}': {Error}. Status: {Status}",
                candidatePath, validation.ErrorMessage, validation.Status);

            return validation.Status switch
            {
                PathValidationStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden,
                    new ApiErrorDto(validation.ErrorMessage ?? "Access forbidden.", correlationId)),
                PathValidationStatus.NotFound => StatusCode(StatusCodes.Status404NotFound,
                    new ApiErrorDto(validation.ErrorMessage ?? "Document file not found.", correlationId)),
                _ => StatusCode(StatusCodes.Status400BadRequest,
                    new ApiErrorDto(validation.ErrorMessage ?? "Invalid file request.", correlationId))
            };
        }

        string fullPath = validation.CanonicalPath!;
        string ext = Path.GetExtension(fullPath).ToLowerInvariant();

        // 4. MIME Type Resolution
        string contentType = ext switch
        {
            ".pdf" => "application/pdf",
            ".md" or ".txt" => "text/plain; charset=utf-8",
            _ => "application/octet-stream"
        };

        // 5. Open stream strictly read-only
        var fileStream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 8192,
            useAsync: true);

        string fileName = Path.GetFileName(fullPath);
        Response.Headers.ContentDisposition = $"inline; filename=\"{fileName}\"";

        return File(fileStream, contentType, enableRangeProcessing: true);
    }
}
