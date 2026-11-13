using CityFy.Retrieve.LastFm.Models;
using CityFy.Retrieve.LastFm.Services;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.Errors;
using ServiceDefault.Models;

namespace CityFy.Retrieve.LastFm.Controllers;

/// <summary>
/// Service-to-service endpoints used by CityFy.Api (the only service exposed to browsers): the
/// gateway authenticates the user and passes the user id.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "Internal")]
[Route("internal/lastfm")]
public sealed class InternalLastFmController(
    ILastFmAccountService accounts,
    ILastFmImportService imports,
    IUserDataService userData) : ControllerBase
{
    private const int MaxLimit = 200;

    // GET internal/lastfm/authorize-url — Last.fm page where the user authorizes CityFy
    [HttpGet("authorize-url")]
    public ActionResult<AuthorizeUrlResponse> GetAuthorizeUrl() => Ok(new AuthorizeUrlResponse(accounts.GetAuthorizeUrl()));

    // POST internal/lastfm/link — token of the gateway callback -> linked account (+ history import)
    [HttpPost("link")]
    public async Task<ActionResult<LinkResponse>> Link([FromBody] LinkRequest request, CancellationToken ct) =>
        Ok(await accounts.LinkAsync(request.UserId, request.Token, ct));

    // GET internal/lastfm/accounts — every linked account (admin panel), without session keys
    [HttpGet("accounts")]
    public async Task<ActionResult<IReadOnlyList<LastFmAccountResponse>>> ListAccounts(CancellationToken ct) =>
        Ok(await accounts.ListAsync(ct));

    // GET internal/lastfm/accounts/{userId} — 404 LASTFM_NOT_LINKED
    [HttpGet("accounts/{userId}")]
    public async Task<ActionResult<LastFmAccountResponse>> GetAccount(string userId, CancellationToken ct) =>
        Ok(await accounts.GetAsync(userId, ct));

    // POST internal/lastfm/accounts/{userId}/import — import the whole history again
    [HttpPost("accounts/{userId}/import")]
    public async Task<ActionResult<ImportAcceptedResponse>> StartImport(string userId, CancellationToken ct)
    {
        var importId = await accounts.StartImportAsync(userId, ct);
        return AcceptedAtAction(nameof(GetImport), new { importId }, new ImportAcceptedResponse(importId));
    }

    // POST internal/lastfm/accounts/{userId}/poll — read the new scrobbles at the next tick (admin)
    [HttpPost("accounts/{userId}/poll")]
    public async Task<IActionResult> PollNow(string userId, CancellationToken ct)
    {
        await accounts.PollNowAsync(userId, ct);
        return Accepted();
    }

    // GET internal/lastfm/imports?limit=30 — most recent imports of every user (admin panel)
    [HttpGet("imports")]
    public async Task<ActionResult<IReadOnlyList<ImportResponse>>> ListImports([FromQuery] int limit = 30, CancellationToken ct = default) =>
        Ok((await imports.ListAsync(Math.Clamp(limit, 1, MaxLimit), ct)).Select(ImportResponse.From).ToList());

    // GET internal/lastfm/imports/{importId}
    [HttpGet("imports/{importId}")]
    public async Task<ActionResult<ImportResponse>> GetImport(string importId, CancellationToken ct)
    {
        var task = await imports.GetAsync(importId, ct);
        return task is null
            ? this.CityFyProblem(ErrorCodes.LastFmImportNotFound, $"Last.fm import '{importId}' not found", StatusCodes.Status404NotFound)
            : Ok(ImportResponse.From(task));
    }

    // POST internal/lastfm/imports/{importId}/cancel — admin
    [HttpPost("imports/{importId}/cancel")]
    public async Task<ActionResult<ImportCancelledResponse>> CancelImport(string importId, CancellationToken ct)
    {
        await imports.CancelAsync(importId, ct);
        return Accepted(new ImportCancelledResponse(importId, nameof(ProcessingStatus.Cancelled)));
    }

    // DELETE internal/lastfm/data — EVERY account and import (end of the Last.fm licence, terms 9.3). Admin only, via the gateway
    [HttpDelete("data")]
    public async Task<ActionResult<UserDataDeletion>> DeleteEverything(CancellationToken ct) =>
        Ok(await userData.DeleteEverythingAsync(ct));

    // DELETE internal/lastfm/users/{userId}/data — stop the imports, delete imports and account
    [HttpDelete("users/{userId}/data")]
    public async Task<ActionResult<UserDataDeletion>> DeleteUserData(string userId, CancellationToken ct) =>
        Ok(await userData.DeleteAsync(userId, ct));
}
