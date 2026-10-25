namespace CellAssistant.Infrastructure.Parsing;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Application.DTOs;
using CellAssistant.Application.Ports;
using CellAssistant.Domain.Entities;
using CellAssistant.Domain.Enums;
using Microsoft.Extensions.Logging;

/// <summary>
/// Parses plain text (.txt) files using a 500-word sliding window with 100-word overlap.
/// </summary>
public class PlainTextParser : IDocumentParser
{
    private readonly ILogger<PlainTextParser>? _logger;

    public PlainTextParser(ILogger<PlainTextParser>? logger = null)
    {
        _logger = logger;
    }

    public bool CanParse(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        var ext = Path.GetExtension(filePath);
        return string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ParsedDocumentResult> ParseAsync(
        string filePath,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}", filePath);

        var rawBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var contentHash = ChunkContextEnricher.ComputeSha256(rawBytes);
        var text = Encoding.UTF8.GetString(rawBytes);

        var normalizedRelPath = ChunkContextEnricher.NormalizeRelativePath(relativePath);
        var fileName = Path.GetFileName(filePath);
        var folder = ChunkContextEnricher.ExtractFolder(normalizedRelPath, "General");

        var document = new Document
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            FilePath = Path.GetFullPath(filePath),
            RelativePath = normalizedRelPath,
            DocumentType = DocumentType.Text,
            Folder = folder,
            ContentHash = contentHash,
            EquipmentId = null,
            Manufacturer = null,
            Criticality = null,
            Tags = new List<string>(),
            CreatedUtc = DateTimeOffset.UtcNow,
            ModifiedUtc = File.GetLastWriteTimeUtc(filePath),
            IndexedUtc = DateTimeOffset.UtcNow
        };

        var chunks = new List<DocumentChunk>();
        int chunkIndex = 0;

        if (!string.IsNullOrWhiteSpace(text))
        {
            var wordChunks = ChunkContextEnricher.ChunkWords(text);
            foreach (var wordChunk in wordChunks)
            {
                var header = ChunkContextEnricher.FormatHeader(
                    fileName,
                    folder,
                    equipmentId: null,
                    faultOrAlarmId: null,
                    pageOrHeading: null);

                var enriched = ChunkContextEnricher.EnrichChunk(header, wordChunk);

                var chunk = new DocumentChunk
                {
                    Id = Guid.NewGuid(),
                    DocumentId = document.Id,
                    Content = enriched,
                    ChunkIndex = chunkIndex++,
                    PageNumber = null,
                    SectionHeading = null,
                    EquipmentId = null,
                    FaultId = null,
                    AlarmId = null,
                    Tags = new List<string>()
                };

                chunks.Add(chunk);
            }
        }

        document.ChunkCount = chunks.Count;
        return new ParsedDocumentResult(document, chunks);
    }
}
