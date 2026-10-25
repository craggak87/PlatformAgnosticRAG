namespace CellAssistant.Domain.Services;

using CellAssistant.Domain.Enums;
using CellAssistant.Domain.ValueObjects;

/// <summary>
/// Standard domain implementation of the confidence calculation engine.
/// </summary>
public class ConfidenceCalculator : IConfidenceCalculator
{
    private const double HighThreshold = 0.85;
    private const double HighWithExactMatchThreshold = 0.75;
    private const double MediumThreshold = 0.70;

    public (double Score, ConfidenceLevel Level) Calculate(
        IReadOnlyList<RetrievedChunk>? chunks, 
        bool hasExactIdentifierMatch = false)
    {
        if (chunks == null || chunks.Count == 0)
        {
            return (0.0, ConfidenceLevel.Low);
        }

        double topScore = 0.0;
        bool chunkExactMatch = false;

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            if (chunk.SimilarityScore > topScore)
            {
                topScore = chunk.SimilarityScore;
            }
            if (chunk.ExactIdentifierMatch)
            {
                chunkExactMatch = true;
            }
        }

        bool exactMatch = hasExactIdentifierMatch || chunkExactMatch;

        ConfidenceLevel level;
        if (topScore >= HighThreshold || (exactMatch && topScore >= HighWithExactMatchThreshold))
        {
            level = ConfidenceLevel.High;
        }
        else if (topScore >= MediumThreshold)
        {
            level = ConfidenceLevel.Medium;
        }
        else
        {
            level = ConfidenceLevel.Low;
        }

        return (topScore, level);
    }

    public ConfidenceLevel CalculateLevel(
        IReadOnlyList<RetrievedChunk>? chunks, 
        bool hasExactIdentifierMatch = false)
    {
        return Calculate(chunks, hasExactIdentifierMatch).Level;
    }
}
