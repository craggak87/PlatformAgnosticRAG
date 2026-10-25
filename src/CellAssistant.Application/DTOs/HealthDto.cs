namespace CellAssistant.Application.DTOs;

/// <summary>
/// Status of an individual system component.
/// </summary>
public record ComponentHealthDto(
    string Status, // "Healthy", "Unhealthy"
    string Description = ""
);

/// <summary>
/// Health check response representing subsystem operational status.
/// </summary>
public record HealthDto(
    string Status, // "Healthy", "Degraded", "Unhealthy"
    IReadOnlyDictionary<string, ComponentHealthDto> Components
)
{
    public string Api => Components.TryGetValue("api", out var c) ? c.Status : "Unknown";
    public string Sqlite => Components.TryGetValue("sqlite", out var c) ? c.Status : "Unknown";
    public string Qdrant => Components.TryGetValue("qdrant", out var c) ? c.Status : "Unknown";
    public string Ollama => Components.TryGetValue("ollama", out var c) ? c.Status : "Unknown";
    public string Indexer => Components.TryGetValue("indexer", out var c) ? c.Status : "Unknown";
}

/// <summary>
/// Flattened representation of component health for OpenAPI response.
/// </summary>
public record HealthResponseDto(
    string Api,
    string Sqlite,
    string Qdrant,
    string Ollama,
    string Indexer
);
