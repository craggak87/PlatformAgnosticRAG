namespace CellAssistant.Domain.Services;

using System.Text.RegularExpressions;

/// <summary>
/// Source-generated regular expression extractor for industrial manufacturing identifiers.
/// </summary>
public partial class IndustrialIdentifierExtractor : IIndustrialIdentifierExtractor
{
    [GeneratedRegex(@"\bALM-\d+\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AlarmPattern();

    [GeneratedRegex(@"\bFLT-\d+\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FaultPattern();

    [GeneratedRegex(@"\b(?:[IOBN]\d*):\d+/\d+\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AllenBradleyPattern();

    [GeneratedRegex(@"\b[IQM]\d+\.\d+\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SiemensPattern();

    [GeneratedRegex(@"\b[XY]\d+\.\d+\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BeckhoffPattern();

    [GeneratedRegex(@"\b(?:ROBOT|PLC|CONV|VFD|CAM|HMI)\d+\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EquipmentPattern();

    [GeneratedRegex(@"\b[EP]\d{3,4}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DrawingPattern();

    private static readonly Regex[] AllPatterns =
    [
        AlarmPattern(),
        FaultPattern(),
        AllenBradleyPattern(),
        SiemensPattern(),
        BeckhoffPattern(),
        EquipmentPattern(),
        DrawingPattern()
    ];

    public IReadOnlyList<string> ExtractIdentifiers(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pattern in AllPatterns)
        {
            var matches = pattern.Matches(text);
            for (int i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                if (match.Success)
                {
                    results.Add(match.Value.ToUpperInvariant());
                }
            }
        }

        if (results.Count == 0)
        {
            return Array.Empty<string>();
        }

        return results.ToList();
    }
}
