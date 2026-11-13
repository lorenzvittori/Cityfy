using System.Text.Json;
using CityFy.Api.Auth;
using CityFy.Api.Clients;
using CityFy.Api.Models;
using CityFy.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.Errors;

namespace CityFy.Api.Controllers;

/// <summary>
/// Operations on the services: health, MusicBrainz import, pipeline, Last.fm, uploads, databases.
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
    ILastFmServiceClient lastFm,
    IDumpClient dump,
    IUserDataEraser eraser,
    ILastFmPurgeService purge,
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

    // --- Last.fm accounts and history imports, uploads ------------------------------------

    [HttpGet("lastfm/accounts")]
    public async Task<ActionResult<JsonElement>> GetLastFmAccounts(CancellationToken ct) =>
        Ok(await lastFm.ListAccountsAsync(ct));

    [HttpGet("lastfm/imports")]
    public async Task<ActionResult<JsonElement>> GetLastFmImports([FromQuery] int limit = 30, CancellationToken ct = default) =>
        Ok(await lastFm.ListImportsAsync(Math.Clamp(limit, 1, MaxLimit), ct));

    [HttpPost("lastfm/accounts/{userId}/import")]
    public async Task<ActionResult<JsonElement>> StartLastFmImport(string userId, CancellationToken ct) =>
        Accepted(await lastFm.StartImportAsync(userId, ct));

    [HttpPost("lastfm/accounts/{userId}/poll")]
    public async Task<IActionResult> PollLastFm(string userId, CancellationToken ct)
    {
        await lastFm.PollNowAsync(userId, ct);
        return Accepted();
    }

    [HttpPost("lastfm/imports/{importId}/cancel")]
    public async Task<ActionResult<JsonElement>> CancelLastFmImport(string importId, CancellationToken ct) =>
        Accepted(await lastFm.CancelImportAsync(importId, ct));

    // POST api/admin/lastfm/purge { confirm: "DELETE ALL LASTFM DATA" } — end of the Last.fm licence
    // (terms 9.3): every Last.fm account and all the data of every account, on every service.
    // 502 USER_DATA_DELETION_INCOMPLETE when something is left: repeat.
    [HttpPost("lastfm/purge")]
    public async Task<ActionResult<LastFmPurgeResult>> PurgeLastFm([FromBody] LastFmPurgeRequest request, CancellationToken ct)
    {
        if (request.Confirm != LastFmPurgeRequest.Phrase)
            return this.CityFyProblem(ErrorCodes.ValidationFailed, $"Confirm with \"{LastFmPurgeRequest.Phrase}\"", StatusCodes.Status400BadRequest);
        return Ok(await purge.PurgeAllAsync($"admin {User.Identity?.Name}", ct));
    }

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
