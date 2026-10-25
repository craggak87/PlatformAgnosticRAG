namespace CellAssistant.Application.Options;

using System.ComponentModel.DataAnnotations;

public class OllamaOptions
{
    public const string SectionName = "ollama";

    [Required(ErrorMessage = "Ollama BaseUrl is required."), Url(ErrorMessage = "Ollama BaseUrl must be a valid URL.")]
    public string BaseUrl { get; set; } = "http://localhost:11434";

    [Required(ErrorMessage = "Ollama ChatModel is required.")]
    public string ChatModel { get; set; } = "gemma4:e4b-it-q4_K_M";

    [Required(ErrorMessage = "Ollama FallbackChatModel is required.")]
    public string FallbackChatModel { get; set; } = "qwen3.8:latest";

    [Required(ErrorMessage = "Ollama EmbeddingModel is required.")]
    public string EmbeddingModel { get; set; } = "embeddinggemma-2:latest";

    [Range(10, 600, ErrorMessage = "RequestTimeoutSeconds must be between 10 and 600 seconds.")]
    public int RequestTimeoutSeconds { get; set; } = 120;
}
