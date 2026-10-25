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
using Microsoft.Data.Sqlite;

public class SqliteDocumentRepository : IDocumentRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SqliteDocumentRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task UpsertAsync(Document document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
INSERT INTO documents (
    id, file_name, file_path, relative_path, document_type, folder,
    content_hash, equipment_id, manufacturer, criticality, tags_json,
    chunk_count, created_utc, modified_utc, indexed_utc
) VALUES (
    @id, @file_name, @file_path, @relative_path, @document_type, @folder,
    @content_hash, @equipment_id, @manufacturer, @criticality, @tags_json,
    @chunk_count, @created_utc, @modified_utc, @indexed_utc
)
ON CONFLICT(id) DO UPDATE SET
    file_name = excluded.file_name,
    file_path = excluded.file_path,
    relative_path = excluded.relative_path,
    document_type = excluded.document_type,
    folder = excluded.folder,
    content_hash = excluded.content_hash,
    equipment_id = excluded.equipment_id,
    manufacturer = excluded.manufacturer,
    criticality = excluded.criticality,
    tags_json = excluded.tags_json,
    chunk_count = excluded.chunk_count,
    created_utc = excluded.created_utc,
    modified_utc = excluded.modified_utc,
    indexed_utc = excluded.indexed_utc;
";

        cmd.Parameters.AddWithValue("@id", document.Id.ToString());
        cmd.Parameters.AddWithValue("@file_name", document.FileName ?? string.Empty);
        cmd.Parameters.AddWithValue("@file_path", document.FilePath ?? string.Empty);
        cmd.Parameters.AddWithValue("@relative_path", document.RelativePath ?? string.Empty);
        cmd.Parameters.AddWithValue("@document_type", document.DocumentType.ToString());
        cmd.Parameters.AddWithValue("@folder", document.Folder ?? string.Empty);
        cmd.Parameters.AddWithValue("@content_hash", document.ContentHash ?? string.Empty);
        cmd.Parameters.AddWithValue("@equipment_id", (object?)document.EquipmentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@manufacturer", (object?)document.Manufacturer ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@criticality", (object?)document.Criticality ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tags_json", JsonSerializer.Serialize(document.Tags ?? new List<string>()));
        cmd.Parameters.AddWithValue("@chunk_count", document.ChunkCount);
        cmd.Parameters.AddWithValue("@created_utc", document.CreatedUtc.ToUniversalTime().ToString("O"));
        cmd.Parameters.AddWithValue("@modified_utc", document.ModifiedUtc.ToUniversalTime().ToString("O"));
        cmd.Parameters.AddWithValue("@indexed_utc", document.IndexedUtc.ToUniversalTime().ToString("O"));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
SELECT id, file_name, file_path, relative_path, document_type, folder,
       content_hash, equipment_id, manufacturer, criticality, tags_json,
       chunk_count, created_utc, modified_utc, indexed_utc
FROM documents
WHERE id = @id;
";
        cmd.Parameters.AddWithValue("@id", id.ToString());

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return MapDocument(reader);
        }

        return null;
    }

    public async Task<Document?> GetByFilePathAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
SELECT id, file_name, file_path, relative_path, document_type, folder,
       content_hash, equipment_id, manufacturer, criticality, tags_json,
       chunk_count, created_utc, modified_utc, indexed_utc
FROM documents
WHERE file_path = @file_path;
";
        cmd.Parameters.AddWithValue("@file_path", filePath);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return MapDocument(reader);
        }

        return null;
    }

    public async Task<IReadOnlyList<Document>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
SELECT id, file_name, file_path, relative_path, document_type, folder,
       content_hash, equipment_id, manufacturer, criticality, tags_json,
       chunk_count, created_utc, modified_utc, indexed_utc
FROM documents;
";

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var list = new List<Document>();
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(MapDocument(reader));
        }

        return list;
    }

    public async Task DeleteByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();

        cmd.CommandText = "DELETE FROM documents WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id.ToString());

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();

        cmd.CommandText = "SELECT COUNT(*) FROM documents;";
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    private static Document MapDocument(SqliteDataReader reader)
    {
        var tagsRaw = reader.IsDBNull(10) ? null : reader.GetString(10);
        List<string> tags;
        try
        {
            tags = string.IsNullOrWhiteSpace(tagsRaw)
                ? new List<string>()
                : (JsonSerializer.Deserialize<List<string>>(tagsRaw) ?? new List<string>());
        }
        catch
        {
            tags = new List<string>();
        }

        return new Document
        {
            Id = Guid.Parse(reader.GetString(0)),
            FileName = reader.GetString(1),
            FilePath = reader.GetString(2),
            RelativePath = reader.GetString(3),
            DocumentType = Enum.TryParse<DocumentType>(reader.GetString(4), out var dt) ? dt : DocumentType.Markdown,
            Folder = reader.GetString(5),
            ContentHash = reader.GetString(6),
            EquipmentId = reader.IsDBNull(7) ? null : reader.GetString(7),
            Manufacturer = reader.IsDBNull(8) ? null : reader.GetString(8),
            Criticality = reader.IsDBNull(9) ? null : reader.GetString(9),
            Tags = tags,
            ChunkCount = reader.GetInt32(11),
            CreatedUtc = ParseUtc(reader.GetString(12)),
            ModifiedUtc = ParseUtc(reader.GetString(13)),
            IndexedUtc = ParseUtc(reader.GetString(14))
        };
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
