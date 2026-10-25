namespace CellAssistant.Application.Ports;

public interface IFileWatcherService
{
    /// <summary>
    /// Starts watching the knowledge base root path for Created, Changed, Deleted, Renamed events.
    /// </summary>
    void StartWatching();

    /// <summary>
    /// Stops watching the filesystem.
    /// </summary>
    void StopWatching();

    /// <summary>
    /// Async start support for hosted background services.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Async stop support for hosted background services.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
