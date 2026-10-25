namespace CellAssistant.Application.Options;

using System.ComponentModel.DataAnnotations;

public class KnowledgeBaseOptions
{
    public const string SectionName = "knowledgeBase";

    [Required(ErrorMessage = "KnowledgeBase RootPath is required.")]
    public string RootPath { get; set; } = "./KnowledgeBase";

    [Required(ErrorMessage = "KnowledgeBase SqlitePath is required.")]
    public string SqlitePath { get; set; } = "./data/metadata.db";

    [Range(50, 2000, ErrorMessage = "MaxChunkWords must be between 50 and 2000 words.")]
    public int MaxChunkWords { get; set; } = 500;

    [Range(0, 500, ErrorMessage = "ChunkOverlapWords must be between 0 and 500 words.")]
    public int ChunkOverlapWords { get; set; } = 100;

    [Range(100, 5000, ErrorMessage = "WatcherDebounceMs must be between 100 and 5000 milliseconds.")]
    public int WatcherDebounceMs { get; set; } = 500;
}
