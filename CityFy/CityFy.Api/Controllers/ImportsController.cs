using CityFy.Api.Auth;
using CityFy.Api.Clients;
using CityFy.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.Errors;

namespace CityFy.Api.Controllers;

/// <summary>Ways to bring plays in: upload of the Spotify export, manual Spotify sync, pipeline status.</summary>
[ApiController]
[Authorize]
[ApiExplorerSettings(GroupName = "v1")]
[Route("api/v1")]
public sealed class ImportsController(IDumpClient dump, ISpotifyServiceClient spotify, IOrchestratorClient orchestrator) : ControllerBase
{
    /// <summary>Upload limit of the API; Retrieve.Dump.Spotify enforces its own (200 MB, UPLOAD_TOO_LARGE).</summary>
    public const long MaxUploadBytes = 220_000_000;

    private const int MaxRuns = 50;

    // POST api/v1/uploads (multipart, field "file") — Spotify extended streaming history zip
    [HttpPost("uploads")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<ActionResult<UploadAcceptedDto>> Upload(IFormFile? file, CancellationToken ct)
    {
        if (file == null)
            return this.CityFyProblem(ErrorCodes.UploadFileMissing, "Missing 'file'", StatusCodes.Status400BadRequest);

        await using var stream = file.OpenReadStream();
        var accepted = await dump.UploadAsync(stream, file.FileName, User.UserId(), ct);
        return AcceptedAtAction(nameof(GetUpload), new { uploadId = accepted.Id }, new UploadAcceptedDto(accepted.Id));
    }

    // GET api/v1/uploads/{uploadId}
    [HttpGet("uploads/{uploadId}")]
    public async Task<ActionResult<ImportStatusDto>> GetUpload(string uploadId, CancellationToken ct)
    {
        var task = await dump.GetUploadAsync(uploadId, ct);
        // Someone else's upload looks exactly like a missing one
        return task.UserId == User.UserId()
            ? Ok(task.ToDto())
            : this.CityFyProblem(ErrorCodes.UploadNotFound, $"Upload '{uploadId}' not found", StatusCodes.Status404NotFound);
    }

    // POST api/v1/spotify/sync — read the latest plays from Spotify now (it also runs every 30 min)
    [HttpPost("spotify/sync")]
    public async Task<ActionResult<SyncAcceptedDto>> Sync(CancellationToken ct)
    {
        var job = await spotify.StartRetrieveAsync(User.UserId(), ct);
        return AcceptedAtAction(nameof(GetSync), new { jobId = job.JobId }, new SyncAcceptedDto(job.JobId));
    }

    // GET api/v1/spotify/sync/{jobId}
    [HttpGet("spotify/sync/{jobId}")]
    public async Task<ActionResult<ImportStatusDto>> GetSync(string jobId, CancellationToken ct)
    {
        var job = await spotify.GetJobAsync(jobId, ct);
        return job.UserId == User.UserId()
            ? Ok(job.ToDto())
            : this.CityFyProblem(ErrorCodes.RetrieveJobNotFound, $"Sync '{jobId}' not found", StatusCodes.Status404NotFound);
    }

    // GET api/v1/pipeline/runs?limit=10 — the user's city builds, most recent first
    [HttpGet("pipeline/runs")]
    public async Task<ActionResult<IReadOnlyList<PipelineRunDto>>> GetRuns([FromQuery] int limit = 10, CancellationToken ct = default) =>
        Ok((await orchestrator.ListRunsAsync(User.UserId(), Math.Clamp(limit, 1, MaxRuns), ct)).Select(r => r.ToDto()).ToList());
}
