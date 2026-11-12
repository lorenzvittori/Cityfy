using CityFy.Orchestrator.Models;
using CityFy.Orchestrator.Services;
using Microsoft.AspNetCore.Mvc;

namespace CityFy.Orchestrator.Controllers;

/// <summary>Service-to-service endpoints used by CityFy.Api (user "rebuild" button, admin panel).</summary>
[ApiController]
[ApiExplorerSettings(GroupName = "Internal")]
[Route("internal/pipeline")]
public sealed class InternalPipelineController(IPipelineService pipeline) : ControllerBase
{
    // POST internal/pipeline/trigger/{userId} — rebuild a user's city now, without importing anything
    [HttpPost("trigger/{userId}")]
    public async Task<ActionResult<PipelineRun>> Trigger(string userId, CancellationToken ct) =>
        Accepted(await pipeline.TriggerAsync(userId, ct));
}
