namespace CellAssistant.Application.Ports;

using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Application.DTOs;

public interface IHealthCheckService
{
    /// <summary>
    /// Executes health probes across API, SQLite, Qdrant, Ollama, and Indexer.
    /// </summary>
    Task<HealthDto> CheckHealthAsync(CancellationToken cancellationToken = default);
}
