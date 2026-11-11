using CityFy.Orchestrator.Models;
using CityFy.Orchestrator.Options;
using CityFy.Orchestrator.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ServiceDefault.Errors;

namespace CityFy.Orchestrator.Controllers;

[ApiController]
[ApiExplorerSettings(GroupName = "Orchestrator")]
[Route("api/pipeline/runs")]
public class PipelineController : ControllerBase
{
    private readonly PipelineRunRepository _repo;

    public PipelineController(PipelineRunRepository repo)
    {
        _repo = repo;
    }

    // GET api/pipeline/runs/{runId}
    [HttpGet("{runId}")]
    public async Task<ActionResult<PipelineRun>> Get(string runId, CancellationToken ct)
    {
        var run = await _repo.GetAsync(runId, ct);
        return run == null
            ? this.CityFyProblem(ErrorCodes.PipelineRunNotFound, $"Pipeline run '{runId}' not found", StatusCodes.Status404NotFound)
            : Ok(run);
    }

    // POST api/pipeline/runs/{runId}/cancel — admin: a Pending run never starts, a Building one is closed
    // (Elaboration finishes the build in progress, its answer is ignored)
    [HttpPost("{runId}/cancel")]
    public async Task<IActionResult> Cancel(string runId, CancellationToken ct)
    {
        if (await _repo.GetAsync(runId, ct) == null)
            return this.CityFyProblem(ErrorCodes.PipelineRunNotFound, $"Pipeline run '{runId}' not found", StatusCodes.Status404NotFound);

        return await _repo.CancelAsync(runId, DateTime.UtcNow, ct)
            ? Accepted(await _repo.GetAsync(runId, ct))
            : this.CityFyProblem(ErrorCodes.ProcessNotCancellable, $"Pipeline run '{runId}' is not active", StatusCodes.Status409Conflict);
    }

    // GET api/pipeline/runs?userId=...&limit=20 — most recent first
    [HttpGet]
    public async Task<ActionResult<List<PipelineRun>>> List([FromQuery] string? userId, [FromQuery] int limit = 20, CancellationToken ct = default) =>
        Ok(await _repo.ListAsync(userId, Math.Clamp(limit, 1, 200), ct));
}

/// <summary>Service-to-service endpoint used by CityFy.Api when a user (or the admin) deletes the user's data.</summary>
[ApiController]
[ApiExplorerSettings(GroupName = "Internal")]
[Route("internal/users")]
public class InternalUserDataController : ControllerBase
{
    // DELETE internal/users/{userId}/data — every pipeline run of the user
    [HttpDelete("{userId}/data")]
    public async Task<ActionResult<ServiceDefault.Models.UserDataDeletion>> Delete(string userId, [FromServices] PipelineRunRepository repo, CancellationToken ct) =>
        Ok(new ServiceDefault.Models.UserDataDeletion("Orchestrator",
            new Dictionary<string, long> { ["pipelineRuns"] = await repo.DeleteUserAsync(userId, ct) }));
}

[ApiController]
[Route("debug/pipeline")]
public class DebugPipelineController : ControllerBase
{
    // POST debug/pipeline/trigger/{userId} — rebuild a user's city without importing anything
    [HttpPost("trigger/{userId}")]
    public async Task<ActionResult<PipelineRun>> Trigger(string userId,
        [FromServices] PipelineRunRepository repo, [FromServices] IOptions<PipelineOptions> options, CancellationToken ct)
    {
        var o = options.Value;
        var run = await repo.AddTriggerAsync(userId,
            new PipelineTrigger { Source = "manual", SourceRef = "debug", NewPlays = 0, At = DateTime.UtcNow },
            o.Periods, TimeSpan.Zero, TimeSpan.Zero, ct);
        return Accepted(run);
    }
}
