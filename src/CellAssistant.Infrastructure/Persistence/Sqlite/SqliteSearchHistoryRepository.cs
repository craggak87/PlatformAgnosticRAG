namespace CellAssistant.Infrastructure.Persistence.Sqlite;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Application.Ports;
using CellAssistant.Domain.Entities;
using CellAssistant.Domain.Enums;
using CellAssistant.Domain.ValueObjects;
using Microsoft.Data.Sqlite;

public class SqliteSearchHistoryRepository : ISearchHistoryRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public SqliteSearchHistoryRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task AddAsync(SearchHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
INSERT INTO search_history (id, question, answer, confidence, sources_json, asked_utc)
VALUES (@id, @question, @answer, @confidence, @sources_json, @asked_utc);
";

        cmd.Parameters.AddWithValue("@id", entry.Id.ToString());
        cmd.Parameters.AddWithValue("@question", entry.Question ?? string.Empty);
        cmd.Parameters.AddWithValue("@answer", entry.Answer ?? string.Empty);
        cmd.Parameters.AddWithValue("@confidence", entry.Confidence.ToString());
        cmd.Parameters.AddWithValue("@sources_json", JsonSerializer.Serialize(entry.Sources ?? new List<SourceReference>(), JsonOptions));
        cmd.Parameters.AddWithValue("@asked_utc", entry.AskedUtc.ToUniversalTime().ToString("O"));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SearchHistoryEntry>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        int effectiveLimit = limit <= 0 ? 20 : Math.Min(limit, 100);

        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
SELECT id, question, answer, confidence, sources_json, asked_utc
FROM search_history
ORDER BY asked_utc DESC
LIMIT @limit;
";
        cmd.Parameters.AddWithValue("@limit", effectiveLimit);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var list = new List<SearchHistoryEntry>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var sourcesRaw = reader.IsDBNull(4) ? null : reader.GetString(4);
            List<SourceReference> sources;
            try
            {
                sources = string.IsNullOrWhiteSpace(sourcesRaw)
                    ? new List<SourceReference>()
                    : (JsonSerializer.Deserialize<List<SourceReference>>(sourcesRaw, JsonOptions) ?? new List<SourceReference>());
            }
            catch
            {
                sources = new List<SourceReference>();
            }

            var entry = new SearchHistoryEntry
            {
                Id = Guid.Parse(reader.GetString(0)),
                Question = reader.GetString(1),
                Answer = reader.GetString(2),
                Confidence = Enum.TryParse<ConfidenceLevel>(reader.GetString(3), out var cl) ? cl : ConfidenceLevel.Low,
                Sources = sources,
                AskedUtc = ParseUtc(reader.GetString(5))
            };

            list.Add(entry);
        }

        return list;
    }

    private static DateTimeOffset ParseUtc(string val)
    {
        if (DateTimeOffset.TryParse(val, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
        {
            return dto;
        }
        return DateTimeOffset.UtcNow;
    }
}
