namespace CellAssistant.Application.Options;

using System.ComponentModel.DataAnnotations;

public class ServerOptions
{
    public const string SectionName = "server";

    [Required(ErrorMessage = "Server Host is required.")]
    public string Host { get; set; } = "0.0.0.0";

    [Range(1, 65535, ErrorMessage = "Server Port must be between 1 and 65535.")]
    public int Port { get; set; } = 8080;
}
