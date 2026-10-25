namespace CellAssistant.Infrastructure.Parsing;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

/// <summary>
/// Centralized utility for context header formatting, path normalization, sliding-window chunking, and hashing.
/// </summary>
public static class ChunkContextEnricher
{
    public const int DefaultMaxWords = 500;
    public const int DefaultOverlapWords = 100;
    public const int DefaultStepWords = DefaultMaxWords - DefaultOverlapWords; // 400

    /// <summary>
    /// Normalizes relative paths to POSIX forward slashes ('/') and trims leading slashes.
    /// E.g. "Faults\Robot.md" -> "Faults/Robot.md", "/Alarms/ALM-205.md" -> "Alarms/ALM-205.md".
    /// </summary>
    public static string NormalizeRelativePath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        return path.Replace('\\', '/').TrimStart('/');
    }

    /// <summary>
    /// Extracts the top-level vault folder from a relative path.
    /// E.g. "Faults/Robot.md" -> "Faults", "Manuals/Sub/Robot.pdf" -> "Manuals", "Notes.txt" -> "General".
    /// </summary>
    public static string ExtractFolder(string? relativePath, string defaultFolder = "General")
    {
        var normalized = NormalizeRelativePath(relativePath);
        if (string.IsNullOrEmpty(normalized))
            return defaultFolder;

        int slashIdx = normalized.IndexOf('/');
        return slashIdx > 0 ? normalized[..slashIdx] : defaultFolder;
    }

    /// <summary>
    /// Formats the verbatim bracketed context header:
    /// [Document: {FileName} | Folder: {Folder} | Equipment: {equipment_id} | Fault/Alarm: {fault_id/alarm_id} | Page/Section: {PageOrHeading}]
    /// </summary>
    public static string FormatHeader(
        string fileName,
        string folder,
        string? equipmentId,
        string? faultOrAlarmId,
        string? pageOrHeading)
    {
        var docName = string.IsNullOrWhiteSpace(fileName) ? "Document" : fileName.Trim();
        var fld = string.IsNullOrWhiteSpace(folder) ? "General" : folder.Trim();
        var eq = string.IsNullOrWhiteSpace(equipmentId) ? "N/A" : equipmentId.Trim();
        var fa = string.IsNullOrWhiteSpace(faultOrAlarmId) ? "N/A" : faultOrAlarmId.Trim();
        var ps = string.IsNullOrWhiteSpace(pageOrHeading) ? "N/A" : pageOrHeading.Trim();

        return $"[Document: {docName} | Folder: {fld} | Equipment: {eq} | Fault/Alarm: {fa} | Page/Section: {ps}]";
    }

    /// <summary>
    /// Prepends the formatted header to the content chunk separated by a newline.
    /// </summary>
    public static string EnrichChunk(string header, string content)
    {
        return $"{header}\n{content.Trim()}";
    }

    /// <summary>
    /// Splits text into words and applies a sliding window with overlap:
    /// - If word count &lt;= 500, returns a single chunk containing the full text.
    /// - If word count &gt; 500, splits into 500-word windows with 100-word overlap (step = 400).
    /// </summary>
    public static IReadOnlyList<string> ChunkWords(
        string text,
        int maxWords = DefaultMaxWords,
        int overlapWords = DefaultOverlapWords)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        var trimmed = text.Trim();
        var words = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return Array.Empty<string>();

        if (words.Length <= maxWords)
        {
            return new[] { trimmed };
        }

        int step = maxWords - overlapWords;
        if (step <= 0)
            step = maxWords;

        var chunks = new List<string>();
        int index = 0;
        while (index < words.Length)
        {
            var slice = words.Skip(index).Take(maxWords).ToArray();
            chunks.Add(string.Join(" ", slice));
            if (index + maxWords >= words.Length)
                break;
            index += step;
        }

        return chunks;
    }

    /// <summary>
    /// Computes the lowercase 64-character hexadecimal SHA-256 hash of raw bytes.
    /// </summary>
    public static string ComputeSha256(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
