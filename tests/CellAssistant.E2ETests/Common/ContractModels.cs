using System.Text.Json.Serialization;

namespace CellAssistant.E2ETests.Common;

public enum ConfidenceLevel
{
    High,
    Medium,
    Low
}

public enum DocumentType
{
    Markdown,
    Pdf,
    Text
}

public enum CriticalityLevel
{
    High,
    Medium,
    Low
}

public enum IndexerStatus
{
    Idle,
    Indexing,
    Reindexing,
    Failed,
    Degraded
}

public sealed record QueryRequestDto(
    [property: JsonPropertyName("question")] string Question,
    [property: JsonPropertyName("equipmentIdFilter")] string? EquipmentIdFilter = null,
    [property: JsonPropertyName("folderFilter")] string? FolderFilter = null
);

public sealed record SourceReferenceDto(
    [property: JsonPropertyName("documentId")] Guid DocumentId,
    [property: JsonPropertyName("fileName")] string FileName,
    [property: JsonPropertyName("relativePath")] string RelativePath,
    [property: JsonPropertyName("pageNumber")] int? PageNumber = null,
    [property: JsonPropertyName("sectionHeading")] string? SectionHeading = null
);

public sealed record QueryResponseDto(
    [property: JsonPropertyName("answer")] string Answer,
    [property: JsonPropertyName("confidence")] string Confidence,
    [property: JsonPropertyName("sources")] IReadOnlyList<SourceReferenceDto> Sources
);

public sealed record SearchHistoryEntryDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("question")] string Question,
    [property: JsonPropertyName("answer")] string Answer,
    [property: JsonPropertyName("confidence")] string Confidence,
    [property: JsonPropertyName("sources")] IReadOnlyList<SourceReferenceDto> Sources,
    [property: JsonPropertyName("askedUtc")] DateTime AskedUtc
);

public sealed record HealthResponseDto(
    [property: JsonPropertyName("api")] string Api,
    [property: JsonPropertyName("sqlite")] string Sqlite,
    [property: JsonPropertyName("qdrant")] string Qdrant,
    [property: JsonPropertyName("ollama")] string Ollama,
    [property: JsonPropertyName("indexer")] string Indexer
);

public sealed record ReindexResponseDto(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("triggeredUtc")] DateTime TriggeredUtc
);

public sealed record RetrievedChunkModel(
    string Content,
    double SimilarityScore,
    bool ExactIdentifierMatch,
    SourceReferenceDto Source
);

public sealed record DocumentModel(
    Guid Id,
    string FileName,
    string FilePath,
    string RelativePath,
    DocumentType DocumentType,
    string Folder,
    string ContentHash,
    string? EquipmentId,
    string? Manufacturer,
    string? Criticality,
    IReadOnlyList<string> Tags,
    int ChunkCount,
    DateTime CreatedUtc,
    DateTime ModifiedUtc,
    DateTime IndexedUtc
);

public sealed record DocumentChunkModel(
    Guid Id,
    Guid DocumentId,
    string Content,
    int ChunkIndex,
    int? PageNumber,
    string? SectionHeading,
    string? EquipmentId,
    string? FaultId,
    string? AlarmId,
    IReadOnlyList<string> Tags
);

public sealed record VectorPointModel(
    Guid ChunkId,
    Guid DocumentId,
    float[] Vector,
    string Content,
    string FileName,
    string RelativePath,
    string Folder,
    int? PageNumber,
    string? SectionHeading,
    string? EquipmentId,
    string? FaultId,
    string? AlarmId,
    IReadOnlyList<string> Tags
);
