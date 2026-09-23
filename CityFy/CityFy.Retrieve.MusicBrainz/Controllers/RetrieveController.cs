using Microsoft.AspNetCore.Mvc;
using CityFy.Retrieve.MusicBrainz.Services;

namespace CityFy.Retrieve.MusicBrainz.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RetrieveController : ControllerBase
{
    private readonly RetrieveService _service;
    private readonly ILogger<RetrieveController> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public RetrieveController(RetrieveService service, ILogger<RetrieveController> logger, IServiceScopeFactory scopeFactory)
    {
        _service = service;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    [HttpPost]
    public async Task<IActionResult> Retrieve(CancellationToken cancellationToken)
    {
        try
        {
            // Run retrieval in a background task that creates its own scope so scoped services (DbContext, repos)
            // remain valid for the lifetime of the background operation.
            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<RetrieveService>();
                try
                {
                    await svc.RunRetrieveAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background retrieve failed");
                }
            });

            return Accepted();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Retrieve failed");
            return NotFound(ex.Message);
        }
    }
}
