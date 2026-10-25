namespace CellAssistant.Application.Services;

using CellAssistant.Application.DTOs;
using CellAssistant.Domain.ValueObjects;

public interface IContextBuilder
{
    string BuildPrompt(string question, IReadOnlyList<RetrievedChunk>? chunks, int topK = 5);
    IReadOnlyList<SourceReferenceDto> ExtractSources(IReadOnlyList<RetrievedChunk>? chunks);
}
