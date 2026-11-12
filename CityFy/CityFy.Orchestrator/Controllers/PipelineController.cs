using CityFy.Orchestrator.Models;
using CityFy.Orchestrator.Services;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.Errors;

namespace CityFy.Orchestrator.Controllers;

[ApiController]
[ApiExplorerSettings(GroupName = "Orchestrator")]
[Route("api/pipeline/runs")]
public sealed class PipelineController(IPipelineService pipeline) : ControllerBase
{
    // GET api/pipeline/runs/{runId}
    [HttpGet("{runId}")]
    public async Task<ActionResult<PipelineRun>> Get(string runId, CancellationToken ct)
    {
        var run = await pipeline.GetAsync(runId, ct);
        return run == null
            ? this.CityFyProblem(ErrorCodes.PipelineRunNotFound, $"Pipeline run '{runId}' not found", StatusCodes.Status404NotFound)
            : Ok(run);
    }

    // POST api/pipeline/runs/{runId}/cancel — admin: a Pending run never starts, a Building one is closed
    // (Elaboration finishes the build in progress, its answer is ignored)
    [HttpPost("{runId}/cancel")]
    public async Task<ActionResult<PipelineRun>> Cancel(string runId, CancellationToken ct) =>
        Accepted(await pipeline.CancelAsync(runId, ct));

    // GET api/pipeline/runs?userId=...&limit=20 — most recent first
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PipelineRun>>> List([FromQuery] string? userId, [FromQuery] int limit = 20, CancellationToken ct = default) =>
        Ok(await pipeline.ListAsync(userId, Math.Clamp(limit, 1, 200), ct));
}
