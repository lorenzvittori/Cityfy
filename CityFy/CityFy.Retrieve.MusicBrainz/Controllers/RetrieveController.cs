using Microsoft.AspNetCore.Mvc;
using CityFy.Retrieve.MusicBrainz.Services;

namespace CityFy.Retrieve.MusicBrainz.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RetrieveController : ControllerBase
{
    private readonly RetrieveService _service;
    private readonly ILogger<RetrieveController> _logger;

    public RetrieveController(RetrieveService service, ILogger<RetrieveController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Retrieve(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.RunRetrieveAsync(cancellationToken);
            return Ok(new { message = "Import completed", genres = result.Genres, relations = result.Relations });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Retrieve failed");
            return NotFound(ex.Message);
        }
    }
}
