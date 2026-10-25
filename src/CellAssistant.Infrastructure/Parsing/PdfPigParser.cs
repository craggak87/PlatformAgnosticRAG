namespace CellAssistant.Infrastructure.Parsing;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Application.DTOs;
using CellAssistant.Application.Ports;
using CellAssistant.Domain.Entities;
using CellAssistant.Domain.Enums;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

/// <summary>
/// Parses multi-page PDF documents, preserving 1-based page numbers and content reading order.
/// </summary>
public class PdfPigParser : IDocumentParser
{
    private static readonly Regex EquipmentRegex = new(
        @"(?<![a-zA-Z0-9])(ROBOT|PLC|CONV|VFD|CAM|HMI)\d{2}(?![a-zA-Z0-9])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ILogger<PdfPigParser>? _logger;

    public PdfPigParser(ILogger<PdfPigParser>? logger = null)
    {
        _logger = logger;
    }

    public bool CanParse(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        var ext = Path.GetExtension(filePath);
        return string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ParsedDocumentResult> ParseAsync(
        string filePath,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"PDF file not found: {filePath}", filePath);

        var rawBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var contentHash = ChunkContextEnricher.ComputeSha256(rawBytes);

        var normalizedRelPath = ChunkContextEnricher.NormalizeRelativePath(relativePath);
        var fileName = Path.GetFileName(filePath);
        var folder = ChunkContextEnricher.ExtractFolder(normalizedRelPath, "Manuals");
        var equipmentId = InferEquipmentIdFromFileName(fileName);

        var document = new Document
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            FilePath = Path.GetFullPath(filePath),
            RelativePath = normalizedRelPath,
            DocumentType = DocumentType.Pdf,
            Folder = folder,
            ContentHash = contentHash,
            EquipmentId = equipmentId,
            Manufacturer = null,
            Criticality = null,
            Tags = new List<string> { "manual" },
            CreatedUtc = DateTimeOffset.UtcNow,
            ModifiedUtc = File.GetLastWriteTimeUtc(filePath),
            IndexedUtc = DateTimeOffset.UtcNow
        };

        var chunks = new List<DocumentChunk>();
        int chunkIndex = 0;

        // Validate basic PDF header before attempting parse
        if (rawBytes.Length < 4 || rawBytes[0] != '%' || rawBytes[1] != 'P' || rawBytes[2] != 'D' || rawBytes[3] != 'F')
        {
            _logger?.LogWarning("File '{FilePath}' has invalid PDF header bytes. Returning 0 chunks.", filePath);
            document.ChunkCount = 0;
            return new ParsedDocumentResult(document, chunks);
        }

        try
        {
            using var pdfDoc = PdfDocument.Open(rawBytes);
            foreach (var page in pdfDoc.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 1-based page number (PdfPig page.Number is 1-based)
                int pageNumber = page.Number;

                string pageText;
                try
                {
                    pageText = ContentOrderTextExtractor.GetText(page);
                    if (string.IsNullOrWhiteSpace(pageText))
                    {
                        pageText = page.Text;
                    }
                }
                catch
                {
                    pageText = page.Text;
                }

                if (string.IsNullOrWhiteSpace(pageText))
                    continue;

                var wordChunks = ChunkContextEnricher.ChunkWords(pageText);
                var pageHeading = $"Page {pageNumber}";

                foreach (var wordChunk in wordChunks)
                {
                    var header = ChunkContextEnricher.FormatHeader(
                        fileName,
                        folder,
                        equipmentId,
                        faultOrAlarmId: null,
                        pageOrHeading: pageHeading);

                    var enriched = ChunkContextEnricher.EnrichChunk(header, wordChunk);

                    var chunk = new DocumentChunk
                    {
                        Id = Guid.NewGuid(),
                        DocumentId = document.Id,
                        Content = enriched,
                        ChunkIndex = chunkIndex++,
                        PageNumber = pageNumber, // 1-based!
                        SectionHeading = null,
                        EquipmentId = equipmentId,
                        FaultId = null,
                        AlarmId = null,
                        Tags = new List<string> { "manual" }
                    };

                    chunks.Add(chunk);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Failed reading PDF content for '{FilePath}'. Returning partial/empty chunks.", filePath);
        }

        document.ChunkCount = chunks.Count;
        return new ParsedDocumentResult(document, chunks);
    }

    private static string? InferEquipmentIdFromFileName(string fileName)
    {
        var match = EquipmentRegex.Match(fileName);
        return match.Success ? match.Value.ToUpperInvariant() : null;
    }
}
