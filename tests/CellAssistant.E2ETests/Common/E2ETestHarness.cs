using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace CellAssistant.E2ETests.Common;

public static class E2ETestHarness
{
    private static readonly Regex AlarmRegex = new(@"\bALM-\d{3,4}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FaultRegex = new(@"\bFLT-\d{3,4}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PlcAbRegex = new(@"\b[IO]:\d+/\d+\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PlcSiemensRegex = new(@"\b[IQ]\d+\.\d+\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PlcBeckhoffRegex = new(@"\b[XY]\d+\.\d+\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EquipmentRegex = new(@"\b(ROBOT|PLC|CONV|VFD|CAM|HMI)\d{2}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DrawingRegex = new(@"\b[EP]\d{3}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<string> ExtractIndustrialIdentifiers(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in AlarmRegex.Matches(text)) set.Add(match.Value.ToUpperInvariant());
        foreach (Match match in FaultRegex.Matches(text)) set.Add(match.Value.ToUpperInvariant());
        foreach (Match match in PlcAbRegex.Matches(text)) set.Add(match.Value.ToUpperInvariant());
        foreach (Match match in PlcSiemensRegex.Matches(text)) set.Add(match.Value.ToUpperInvariant());
        foreach (Match match in PlcBeckhoffRegex.Matches(text)) set.Add(match.Value.ToUpperInvariant());
        foreach (Match match in EquipmentRegex.Matches(text)) set.Add(match.Value.ToUpperInvariant());
        foreach (Match match in DrawingRegex.Matches(text)) set.Add(match.Value.ToUpperInvariant());

        return set.ToList();
    }

    public static (double Score, ConfidenceLevel Level) CalculateConfidence(
        IReadOnlyList<RetrievedChunkModel> chunks,
        bool hasExactIdentifierMatch)
    {
        if (chunks == null || chunks.Count == 0)
        {
            return (0.0, ConfidenceLevel.Low);
        }

        double maxScore = chunks.Max(c => c.SimilarityScore);
        bool match = hasExactIdentifierMatch || chunks.Any(c => c.ExactIdentifierMatch);

        if (maxScore >= SpecificationConstants.HighConfidenceNormalMin ||
            (match && maxScore >= SpecificationConstants.HighConfidenceExactMatchMin))
        {
            return (maxScore, ConfidenceLevel.High);
        }

        if (maxScore >= SpecificationConstants.MediumConfidenceMin)
        {
            return (maxScore, ConfidenceLevel.Medium);
        }

        return (maxScore, ConfidenceLevel.Low);
    }

    public static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        return path.Replace('\\', '/').TrimStart('/');
    }

    public static string FormatChunkHeader(
        string fileName,
        string folder,
        string? equipmentId,
        string? faultOrAlarmId,
        string? pageOrHeading,
        string content)
    {
        var eq = string.IsNullOrWhiteSpace(equipmentId) ? "N/A" : equipmentId;
        var fa = string.IsNullOrWhiteSpace(faultOrAlarmId) ? "N/A" : faultOrAlarmId;
        var ps = string.IsNullOrWhiteSpace(pageOrHeading) ? "N/A" : pageOrHeading;

        return $"[Document: {fileName} | Folder: {folder} | Equipment: {eq} | Fault/Alarm: {fa} | Page/Section: {ps}]\n{content}";
    }

    public static string ComputeSha256(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static SqliteConnection CreateInMemorySqlite()
    {
        var dbName = $"memdb_{Guid.NewGuid():N}";
        var conn = new SqliteConnection($"Data Source={dbName};Mode=Memory;");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL;" +
                          SpecificationConstants.SqliteDocumentsTableDdl +
                          SpecificationConstants.SqliteSearchHistoryTableDdl +
                          SpecificationConstants.SqliteIndexerStateTableDdl;
        cmd.ExecuteNonQuery();

        return conn;
    }
}
