namespace CellAssistant.Application.Services;

using System.Text;
using CellAssistant.Application.DTOs;
using CellAssistant.Domain.ValueObjects;

public class ContextBuilder : IContextBuilder
{
    public const string SystemPrompt =
        "You are an engineering assistant.\n\n" +
        "Answer only with supplied context.\n\n" +
        "Do not invent information.\n\n" +
        "Always cite sources.\n\n" +
        "If data cannot be found, clearly state:\n" +
        "\"The available documentation does not contain sufficient information.\"";

    public const string InsufficientInformationFallback =
        "The available documentation does not contain sufficient information.";

    /// <summary>
    /// Builds the full prompt containing system instructions, context chunks, and user question.
    /// </summary>
    public string BuildPrompt(string question, IReadOnlyList<RetrievedChunk>? chunks, int topK = 5)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("Question cannot be null or empty.", nameof(question));

        var sb = new StringBuilder();
        sb.AppendLine(SystemPrompt);
        sb.AppendLine();
        sb.AppendLine("Context:");

        if (chunks != null && chunks.Count > 0)
        {
            var orderedChunks = chunks
                .OrderByDescending(c => c.SimilarityScore)
                .Take(topK);

            foreach (var chunk in orderedChunks)
            {
                sb.AppendLine(chunk.Content);
                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine("No relevant documentation found.");
            sb.AppendLine();
        }

        sb.Append("Question: ");
        sb.AppendLine(question.Trim());

        return sb.ToString();
    }

    /// <summary>
    /// Maps and deduplicates retrieved chunks to source reference DTOs.
    /// </summary>
    public IReadOnlyList<SourceReferenceDto> ExtractSources(IReadOnlyList<RetrievedChunk>? chunks)
    {
        if (chunks == null || chunks.Count == 0)
            return Array.Empty<SourceReferenceDto>();

        return chunks
            .Select(c => c.Source)
            .GroupBy(s => new { s.DocumentId, s.PageNumber, s.SectionHeading })
            .Select(g => g.First())
            .Select(s => new SourceReferenceDto(
                s.DocumentId,
                s.FileName,
                s.RelativePath,
                s.PageNumber,
                s.SectionHeading))
            .ToList();
    }
}
