namespace CellAssistant.Api.Controllers;

using System;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Application.DTOs;
using CellAssistant.Application.Ports;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/health")]
[Produces("application/json")]
public class HealthController : ControllerBase
{
    private readonly IHealthCheckService _healthService;

    public HealthController(IHealthCheckService healthService)
    {
        _healthService = healthService ?? throw new ArgumentNullException(nameof(healthService));
    }

    /// <summary>
    /// Evaluates operational health of API, SQLite, Qdrant, Ollama, and Indexer.
    /// Returns HTTP 200 when healthy, or HTTP 503 when unhealthy/degraded.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(HealthDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HealthDto), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var health = await _healthService.CheckHealthAsync(cancellationToken);

        if (health.Status.Equals("Healthy", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(health);
        }

        return StatusCode(StatusCodes.Status503ServiceUnavailable, health);
    }
}
