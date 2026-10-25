namespace CellAssistant.Api.Controllers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CellAssistant.Application.DTOs;
using CellAssistant.Application.Ports;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/history")]
[Produces("application/json")]
public class HistoryController : ControllerBase
{
    private readonly ISearchHistoryRepository _historyRepository;

    public HistoryController(ISearchHistoryRepository historyRepository)
    {
        _historyRepository = historyRepository ?? throw new ArgumentNullException(nameof(historyRepository));
    }

    /// <summary>
    /// Retrieves recent search history queries in descending chronological order.
    /// </summary>
    /// <param name="limit">Maximum records to retrieve (default 20, max 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SearchHistoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecentHistoryAsync(
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        int clampedLimit = Math.Clamp(limit <= 0 ? 20 : limit, 1, 100);
        var entries = await _historyRepository.GetRecentAsync(clampedLimit, cancellationToken);

        var dtos = entries.Select(e => new SearchHistoryDto(
            e.Id,
            e.Question,
            e.Answer,
            e.Confidence.ToString(),
            e.Sources.Select(s => new SourceReferenceDto(
                s.DocumentId,
                s.FileName,
                s.RelativePath,
                s.PageNumber,
                s.SectionHeading
            )).ToList(),
            e.AskedUtc
        )).ToList();

        return Ok(dtos);
    }
}
