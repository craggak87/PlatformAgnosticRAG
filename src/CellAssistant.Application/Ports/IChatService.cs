namespace CellAssistant.Application.Ports;

public interface IChatService
{
    /// <summary>
    /// Generates a complete text response synchronously from the grounded prompt.
    /// </summary>
    Task<string> GenerateAnswerAsync(string prompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams generated tokens in real-time as they are emitted by Ollama.
    /// </summary>
    Task<string> StreamAnswerAsync(
        string prompt,
        Func<string, Task> onToken,
        CancellationToken cancellationToken = default);
}
