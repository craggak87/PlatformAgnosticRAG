namespace CellAssistant.Domain.Services;

/// <summary>
/// Extracts exact industrial identifiers (alarms, PLC I/O addresses, equipment IDs, drawing numbers) from text.
/// </summary>
public interface IIndustrialIdentifierExtractor
{
    /// <summary>
    /// Scans text and returns a de-duplicated list of normalized uppercase industrial identifiers.
    /// </summary>
    /// <param name="text">Input query or document content.</param>
    /// <returns>Read-only list of unique extracted identifiers.</returns>
    IReadOnlyList<string> ExtractIdentifiers(string? text);
}
