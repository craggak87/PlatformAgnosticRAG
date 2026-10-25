namespace CellAssistant.Application.Ports;

using CellAssistant.Application.DTOs;

public interface IDocumentParser
{
    /// <summary>
    /// Checks whether this parser handles the given file path or extension.
    /// </summary>
    bool CanParse(string filePath);

    /// <summary>
    /// Parses a document from disk, extracting metadata, frontmatter/page numbers,
    /// chunking text with 500w window / 100w overlap, and prepending context headers.
    /// </summary>
    Task<ParsedDocumentResult> ParseAsync(
        string filePath,
        string relativePath,
        CancellationToken cancellationToken = default);
}
