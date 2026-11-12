using System.Text.Json;
using CityFy.Api.Auth;
using CityFy.Api.Clients;
using CityFy.Api.Models;
using CityFy.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CityFy.Api.Controllers;

/// <summary>
/// Operations on the services: health, MusicBrainz import, pipeline, Spotify, uploads, databases.
/// Downstream admin data is passed through as is (the panel shows it, the gateway does not interpret it).
/// </summary>
[ApiController]
[AdminEnabled]
[Authorize(Policy = AuthSetup.AdminPolicy)]
[ApiExplorerSettings(GroupName = "admin")]
[Route("api/admin")]
public sealed class AdminController(
    IServiceHealthService health,
    IMusicBrainzServiceClient musicBrainz,
    IOrchestratorClient orchestrator,
    ISpotifyServiceClient spotify,
    IDumpClient dump,
    IUserDataEraser eraser,
    IDatabaseBrowser db) : ControllerBase
{
    private const int MaxLimit = 200;

    // GET api/admin/services — /health of every service (the Api itself answers in-process)
    [HttpGet("services")]
    public async Task<ActionResult<ServicesOverviewDto>> GetServices(CancellationToken ct) =>
        Ok(await health.GetOverviewAsync(ct));

    // --- MusicBrainz catalog ---------------------------------------------------------

    [HttpGet("musicbrainz/import")]
    public async Task<ActionResult<JsonElement>> GetImport(CancellationToken ct) =>
        Ok(await musicBrainz.GetImportStatusAsync(ct));

    [HttpPost("musicbrainz/import")]
    public async Task<ActionResult<JsonElement>> StartImport(CancellationToken ct) =>
        Accepted(await musicBrainz.StartImportAsync(ct));

    // --- Pipeline (orchestrator) -------------------------------------------------------

    [HttpGet("pipeline/runs")]
    public async Task<ActionResult<List<PipelineRunInfo>>> GetRuns([FromQuery] int limit = 50, CancellationToken ct = default) =>
        Ok(await orchestrator.ListAllRunsAsync(Math.Clamp(limit, 1, MaxLimit), ct));

    [HttpPost("pipeline/rebuild/{userId}")]
    public async Task<ActionResult<PipelineRunInfo>> Rebuild(string userId, CancellationToken ct) =>
        Accepted(await orchestrator.TriggerAsync(userId, ct));

    // --- Spotify accounts and uploads --------------------------------------------------

    [HttpGet("spotify/accounts")]
    public async Task<ActionResult<List<SpotifyAccountInfo>>> GetAccounts(CancellationToken ct) =>
        Ok(await spotify.ListAccountsAsync(ct));

    [HttpGet("spotify/jobs")]
    public async Task<ActionResult<JsonElement>> GetJobs([FromQuery] int limit = 30, CancellationToken ct = default) =>
        Ok(await spotify.ListJobsAsync(Math.Clamp(limit, 1, MaxLimit), ct));

    [HttpPost("spotify/accounts/{userId}/sync")]
    public async Task<ActionResult<JobAccepted>> Sync(string userId, CancellationToken ct) =>
        Accepted(await spotify.StartRetrieveAsync(userId, ct));

    [HttpGet("uploads")]
    public async Task<ActionResult<JsonElement>> GetUploads([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        Ok(await dump.ListUploadsAsync(Math.Max(1, page), Math.Clamp(pageSize, 1, MaxLimit), ct));

    // --- Cancellation -----------------------------------------------------------------
    // 404 when the id does not exist, 409 PROCESS_NOT_CANCELLABLE when it already finished.

    [HttpPost("musicbrainz/import/cancel")]
    public async Task<ActionResult<JsonElement>> CancelImport(CancellationToken ct) =>
        Accepted(await musicBrainz.CancelImportAsync(ct));

    [HttpPost("uploads/{uploadId}/cancel")]
    public async Task<ActionResult<JsonElement>> CancelUpload(string uploadId, CancellationToken ct) =>
        Accepted(await dump.CancelUploadAsync(uploadId, ct));

    [HttpPost("spotify/jobs/{jobId}/cancel")]
    public async Task<ActionResult<JsonElement>> CancelSpotifyJob(string jobId, CancellationToken ct) =>
        Accepted(await spotify.CancelJobAsync(jobId, ct));

    [HttpPost("pipeline/runs/{runId}/cancel")]
    public async Task<ActionResult<JsonElement>> CancelRun(string runId, CancellationToken ct) =>
        Accepted(await orchestrator.CancelRunAsync(runId, ct));

    // --- User data ----------------------------------------------------------------------

    // DELETE api/admin/users/{userId}/data — everything CityFy keeps about that user (same as the
    // user's own "delete my data"); the user's sessions end at their next request
    [HttpDelete("users/{userId}/data")]
    public async Task<ActionResult<UserDataDeletionResult>> DeleteUserData(string userId, CancellationToken ct) =>
        Ok(await eraser.DeleteAsync(userId, $"admin {User.Identity?.Name}", ct));

    // --- Databases (read-only) ---------------------------------------------------------

    [HttpGet("db/mongo")]
    public async Task<ActionResult<IReadOnlyList<TableInfo>>> GetCollections(CancellationToken ct) =>
        Ok(await db.ListCollectionsAsync(ct));

    [HttpGet("db/mongo/{name}")]
    public async Task<ActionResult<TablePage>> GetCollection(string name, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Ok(await db.GetCollectionPageAsync(name, page, pageSize, ct));

    [HttpGet("db/sql")]
    public async Task<ActionResult<IReadOnlyList<TableInfo>>> GetTables(CancellationToken ct) =>
        Ok(await db.ListTablesAsync(ct));

    [HttpGet("db/sql/{name}")]
    public async Task<ActionResult<TablePage>> GetTable(string name, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Ok(await db.GetTablePageAsync(name, page, pageSize, ct));
}
