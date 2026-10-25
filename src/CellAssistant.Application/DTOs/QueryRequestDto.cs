namespace CellAssistant.Application.DTOs;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Natural language industrial engineering query request.
/// Supports both (Question, EquipmentIdFilter, FolderFilter) and (Query, EquipmentId, Folder, TopK).
/// </summary>
public record QueryRequestDto
{
    [Required(ErrorMessage = "Question is required.")]
    [MinLength(1, ErrorMessage = "Question must not be empty.")]
    [MaxLength(2000, ErrorMessage = "Question cannot exceed 2000 characters.")]
    public string Question { get; init; } = string.Empty;

    public string? Query
    {
        get => Question;
        init => Question = string.IsNullOrEmpty(Question) ? (value ?? string.Empty) : Question;
    }

    [MaxLength(100, ErrorMessage = "EquipmentIdFilter cannot exceed 100 characters.")]
    public string? EquipmentIdFilter { get; init; }

    public string? EquipmentId
    {
        get => EquipmentIdFilter;
        init => EquipmentIdFilter ??= value;
    }

    [MaxLength(100, ErrorMessage = "FolderFilter cannot exceed 100 characters.")]
    public string? FolderFilter { get; init; }

    public string? Folder
    {
        get => FolderFilter;
        init => FolderFilter ??= value;
    }

    public int? TopK { get; init; } = 5;

    public QueryRequestDto() { }

    public QueryRequestDto(
        string question,
        string? equipmentIdFilter = null,
        string? folderFilter = null,
        int? topK = 5)
    {
        Question = question;
        EquipmentIdFilter = equipmentIdFilter;
        FolderFilter = folderFilter;
        TopK = topK;
    }
}
