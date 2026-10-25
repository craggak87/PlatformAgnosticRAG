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
using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using Microsoft.Extensions.Logging;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

/// <summary>
/// Parses Markdown documents with YAML frontmatter, extracting structured metadata,
/// inline hashtags, wikilinks, splitting along headings, and applying a 500-word sliding window.
/// </summary>
public class MarkdigYamlParser : IDocumentParser
{
    private static readonly Regex InlineTagRegex = new(
        @"(?<!\S)#([a-zA-Z0-9_\-]+)\b",
        RegexOptions.Compiled);

    private readonly MarkdownPipeline _pipeline;
    private readonly IDeserializer _yamlDeserializer;
    private readonly ILogger<MarkdigYamlParser>? _logger;

    public MarkdigYamlParser(ILogger<MarkdigYamlParser>? logger = null)
    {
        _logger = logger;
        _pipeline = new MarkdownPipelineBuilder()
            .UseYamlFrontMatter()
            .Build();

        _yamlDeserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public bool CanParse(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        var ext = Path.GetExtension(filePath);
        return string.Equals(ext, ".md", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ParsedDocumentResult> ParseAsync(
        string filePath,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Markdown file not found: {filePath}", filePath);

        var rawBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var contentHash = ChunkContextEnricher.ComputeSha256(rawBytes);
        var rawText = Encoding.UTF8.GetString(rawBytes);

        var normalizedRelPath = ChunkContextEnricher.NormalizeRelativePath(relativePath);
        var fileName = Path.GetFileName(filePath);
        var folder = ChunkContextEnricher.ExtractFolder(normalizedRelPath, "General");

        // 1. Parse AST with Markdig
        var mdDoc = Markdown.Parse(rawText, _pipeline);

        // 2. Extract YAML Frontmatter
        var yamlBlock = mdDoc.Descendants<YamlFrontMatterBlock>().FirstOrDefault();
        Dictionary<string, object>? frontmatter = null;
        int bodyStartIndex = 0;

        if (yamlBlock != null)
        {
            bodyStartIndex = yamlBlock.Span.End + 1;
            var yamlText = yamlBlock.Lines.ToString();
            if (!string.IsNullOrWhiteSpace(yamlText))
            {
                try
                {
                    frontmatter = _yamlDeserializer.Deserialize<Dictionary<string, object>>(yamlText);
                }
                catch (YamlException ex)
                {
                    _logger?.LogWarning(ex, "Malformed YAML frontmatter in file '{FilePath}'. Skipping frontmatter parsing.", filePath);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Unexpected error reading YAML frontmatter in '{FilePath}'.", filePath);
                }
            }
        }

        // Extract metadata fields
        string? equipmentId = GetFrontmatterString(frontmatter, "equipment_id");
        string? manufacturer = GetFrontmatterString(frontmatter, "manufacturer");
        string? criticality = GetFrontmatterString(frontmatter, "criticality");
        string? faultId = GetFrontmatterString(frontmatter, "fault_id");
        string? alarmId = GetFrontmatterString(frontmatter, "alarm_id");

        // Collect tags from frontmatter
        var tagSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (frontmatter != null && frontmatter.TryGetValue("tags", out var rawTags) && rawTags != null)
        {
            if (rawTags is IEnumerable<object> tagList)
            {
                foreach (var tag in tagList)
                {
                    var cleanTag = CleanTag(tag?.ToString());
                    if (!string.IsNullOrEmpty(cleanTag))
                        tagSet.Add(cleanTag);
                }
            }
            else if (rawTags is string tagString)
            {
                foreach (var part in tagString.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var cleanTag = CleanTag(part);
                    if (!string.IsNullOrEmpty(cleanTag))
                        tagSet.Add(cleanTag);
                }
            }
        }

        // 3. Extract body text after frontmatter
        string bodyText = bodyStartIndex < rawText.Length
            ? rawText.Substring(bodyStartIndex)
            : string.Empty;

        // Extract inline #tags from body
        foreach (Match match in InlineTagRegex.Matches(bodyText))
        {
            var cleanTag = CleanTag(match.Groups[1].Value);
            if (!string.IsNullOrEmpty(cleanTag))
                tagSet.Add(cleanTag);
        }

        var document = new Document
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            FilePath = Path.GetFullPath(filePath),
            RelativePath = normalizedRelPath,
            DocumentType = DocumentType.Markdown,
            Folder = folder,
            ContentHash = contentHash,
            EquipmentId = equipmentId,
            Manufacturer = manufacturer,
            Criticality = criticality,
            Tags = tagSet.OrderBy(t => t).ToList(),
            CreatedUtc = DateTimeOffset.UtcNow,
            ModifiedUtc = File.GetLastWriteTimeUtc(filePath),
            IndexedUtc = DateTimeOffset.UtcNow
        };

        // 4. Heading-aware chunking
        var headings = mdDoc.Descendants<HeadingBlock>()
            .Where(h => h.Level >= 1 && h.Level <= 3 && h.Span.Start >= bodyStartIndex)
            .OrderBy(h => h.Span.Start)
            .ToList();

        var sections = new List<(string? Heading, string Content)>();

        if (headings.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(bodyText))
            {
                sections.Add((null, bodyText.Trim()));
            }
        }
        else
        {
            // Check for intro content before first heading
            int firstHeadingStart = headings[0].Span.Start;
            if (firstHeadingStart > bodyStartIndex)
            {
                int introLen = firstHeadingStart - bodyStartIndex;
                if (introLen > 0)
                {
                    var intro = rawText.Substring(bodyStartIndex, introLen).Trim();
                    if (!string.IsNullOrWhiteSpace(intro))
                    {
                        sections.Add((null, intro));
                    }
                }
            }

            for (int i = 0; i < headings.Count; i++)
            {
                var currentHeading = headings[i];
                int sectionStart = currentHeading.Span.Start;
                int sectionEnd = (i + 1 < headings.Count)
                    ? headings[i + 1].Span.Start
                    : rawText.Length;

                int sectionLen = sectionEnd - sectionStart;
                if (sectionLen <= 0)
                    continue;

                var sectionRaw = rawText.Substring(sectionStart, sectionLen).Trim();
                if (string.IsNullOrWhiteSpace(sectionRaw))
                    continue;

                // Extract heading title (the first line stripped of leading '#' chars)
                string headingTitle;
                using (var reader = new StringReader(sectionRaw))
                {
                    var firstLine = reader.ReadLine() ?? string.Empty;
                    headingTitle = firstLine.TrimStart('#').Trim();
                }

                sections.Add((headingTitle, sectionRaw));
            }
        }

        // 5. Sliding window chunking per section and header enrichment
        var chunks = new List<DocumentChunk>();
        int chunkIndex = 0;
        string? faultOrAlarmId = faultId ?? alarmId;

        foreach (var (heading, sectionContent) in sections)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var wordChunks = ChunkContextEnricher.ChunkWords(sectionContent);
            foreach (var wordChunk in wordChunks)
            {
                var header = ChunkContextEnricher.FormatHeader(
                    fileName,
                    folder,
                    equipmentId,
                    faultOrAlarmId,
                    heading);

                var enriched = ChunkContextEnricher.EnrichChunk(header, wordChunk);

                var chunk = new DocumentChunk
                {
                    Id = Guid.NewGuid(),
                    DocumentId = document.Id,
                    Content = enriched,
                    ChunkIndex = chunkIndex++,
                    PageNumber = null,
                    SectionHeading = heading,
                    EquipmentId = equipmentId,
                    FaultId = faultId,
                    AlarmId = alarmId,
                    Tags = tagSet.OrderBy(t => t).ToList()
                };

                chunks.Add(chunk);
            }
        }

        document.ChunkCount = chunks.Count;
        return new ParsedDocumentResult(document, chunks);
    }

    private static string? GetFrontmatterString(Dictionary<string, object>? frontmatter, string key)
    {
        if (frontmatter != null && frontmatter.TryGetValue(key, out var val) && val != null)
        {
            var str = val.ToString()?.Trim();
            return string.IsNullOrWhiteSpace(str) ? null : str;
        }
        return null;
    }

    private static string? CleanTag(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw.Trim().TrimStart('#').ToLowerInvariant();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
