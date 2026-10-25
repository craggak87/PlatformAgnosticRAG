namespace CellAssistant.Domain.Services;

using CellAssistant.Domain.Enums;
using CellAssistant.Domain.ValueObjects;

/// <summary>
/// Evaluates retrieval similarity scores and exact identifier matches to determine answer confidence.
/// </summary>
public interface IConfidenceCalculator
{
    /// <summary>
    /// Calculates the numeric score and qualitative confidence level.
    /// </summary>
    /// <param name="chunks">Retrieved chunks from hybrid search.</param>
    /// <param name="hasExactIdentifierMatch">Whether an exact identifier match was verified.</param>
    /// <returns>A tuple of the top similarity score and the corresponding ConfidenceLevel.</returns>
    (double Score, ConfidenceLevel Level) Calculate(IReadOnlyList<RetrievedChunk>? chunks, bool hasExactIdentifierMatch = false);

    /// <summary>
    /// Convenience method to evaluate and return only the ConfidenceLevel.
    /// </summary>
    ConfidenceLevel CalculateLevel(IReadOnlyList<RetrievedChunk>? chunks, bool hasExactIdentifierMatch = false);
}
