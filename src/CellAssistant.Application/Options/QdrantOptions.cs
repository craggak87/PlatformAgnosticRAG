namespace CellAssistant.Application.Options;

using System.ComponentModel.DataAnnotations;

public class QdrantOptions
{
    public const string SectionName = "qdrant";

    [Required(ErrorMessage = "Qdrant Host is required.")]
    public string Host { get; set; } = "localhost";

    [Range(1, 65535, ErrorMessage = "Qdrant HttpPort must be between 1 and 65535.")]
    public int HttpPort { get; set; } = 6333;

    [Range(1, 65535, ErrorMessage = "Qdrant GrpcPort must be between 1 and 65535.")]
    public int GrpcPort { get; set; } = 6334;

    [Required(ErrorMessage = "Qdrant CollectionName is required.")]
    public string CollectionName { get; set; } = "cell-knowledge";

    [Range(128, 4096, ErrorMessage = "Qdrant VectorSize must be between 128 and 4096.")]
    public int VectorSize { get; set; } = 768;
}
