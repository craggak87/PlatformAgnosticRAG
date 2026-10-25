namespace CellAssistant.E2ETests.Common;

public static class SpecificationConstants
{
    // ADR-011 & Doc 4 Exact Fallback
    public const string ExactFallbackMessage = "The available documentation does not contain sufficient information.";

    // ADR-003 & Doc 6 Model Names
    public const string PrimaryChatModel = "gemma4:e4b-it-q4_K_M";
    public const string FallbackChatModel = "qwen3.8:latest";
    public const string EmbeddingModel = "embeddinggemma-2:latest";
    public const int DefaultRequestTimeoutSeconds = 120;
    public const string DefaultOllamaBaseUrl = "http://localhost:11434";

    // ADR-004 & Doc 3 Qdrant Specs
    public const string QdrantCollectionName = "cell-knowledge";
    public const int QdrantVectorSize = 768;
    public const string QdrantDistanceMetric = "Cosine";

    public static readonly string[] QdrantPayloadIndexFields =
    [
        "content",
        "documentId",
        "equipmentId",
        "faultId",
        "alarmId",
        "tags",
        "folder"
    ];

    // ADR-009 & Doc 3 Chunking & Watcher Defaults
    public const int DefaultMaxChunkWords = 500;
    public const int DefaultChunkOverlapWords = 100;
    public const int DefaultWatcherDebounceMs = 500;

    // Doc 4 & Doc 7 Confidence Thresholds
    public const double HighConfidenceNormalMin = 0.85;
    public const double HighConfidenceExactMatchMin = 0.75;
    public const double MediumConfidenceMin = 0.70;

    // Doc 9 16 Standard Vault Folders
    public static readonly string[] StandardVaultFolders =
    [
        "Equipment",
        "Faults",
        "PLC",
        "Robot",
        "Vision",
        "Safety",
        "Electrical",
        "Pneumatics",
        "Mechanical",
        "Maintenance",
        "Alarms",
        "Procedures",
        "Improvements",
        "Lessons-Learned",
        "Drawings",
        "Manuals"
    ];

    // Prohibited Cloud AI SDKs (ADR-002, ADR-013)
    public static readonly string[] ProhibitedCloudPackages =
    [
        "Azure.AI.OpenAI",
        "Azure.AI.Inference",
        "OpenAI",
        "Anthropic",
        "Microsoft.SemanticKernel",
        "LangChain",
        "Amazon.Bedrock",
        "Google.Cloud.AIPlatform"
    ];

    // REST API Routes (Doc 2, Doc 6, Doc 7)
    public const string QueryEndpoint = "/api/query";
    public const string StreamQueryEndpoint = "/api/query/stream";
    public const string DocumentFileEndpointTemplate = "/api/documents/{0}/file";
    public const string HistoryEndpoint = "/api/history";
    public const string HealthEndpoint = "/api/health";
    public const string AdminReindexEndpoint = "/api/admin/reindex";

    // SQLite Schema DDL (Doc 3 §5)
    public const string SqliteDocumentsTableDdl = @"
CREATE TABLE IF NOT EXISTS documents (
    id TEXT PRIMARY KEY,
    file_name TEXT NOT NULL,
    file_path TEXT NOT NULL UNIQUE,
    relative_path TEXT NOT NULL,
    document_type TEXT NOT NULL,
    folder TEXT NOT NULL,
    content_hash TEXT NOT NULL,
    equipment_id TEXT,
    manufacturer TEXT,
    criticality TEXT,
    tags_json TEXT NOT NULL DEFAULT '[]',
    chunk_count INTEGER NOT NULL DEFAULT 0,
    created_utc TEXT NOT NULL,
    modified_utc TEXT NOT NULL,
    indexed_utc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_documents_relative_path ON documents(relative_path);
CREATE INDEX IF NOT EXISTS idx_documents_equipment_id ON documents(equipment_id);";

    public const string SqliteSearchHistoryTableDdl = @"
CREATE TABLE IF NOT EXISTS search_history (
    id TEXT PRIMARY KEY,
    question TEXT NOT NULL,
    answer TEXT NOT NULL,
    confidence TEXT NOT NULL,
    sources_json TEXT NOT NULL DEFAULT '[]',
    asked_utc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_search_history_asked_utc ON search_history(asked_utc DESC);";

    public const string SqliteIndexerStateTableDdl = @"
CREATE TABLE IF NOT EXISTS indexer_state (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    status TEXT NOT NULL,
    last_run_utc TEXT,
    last_error TEXT,
    reindex_requested INTEGER NOT NULL DEFAULT 0
);";
}
