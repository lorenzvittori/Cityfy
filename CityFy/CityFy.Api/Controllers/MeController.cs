using CityFy.Api.Auth;
using CityFy.Api.Clients;
using CityFy.Api.Models;
using CityFy.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.Errors;

namespace CityFy.Api.Controllers;

[ApiController]
[Authorize]
[ApiExplorerSettings(GroupName = "v1")]
[Route("api/v1/me")]
public sealed class MeController(
    ILastFmServiceClient lastFm,
    IUserDataEraser eraser,
    IAnonymousSessionService sessions) : ControllerBase
{
    // GET api/v1/me — who is using the app: anonymous session (with its expiry) or Last.fm account
    // (with the state of the reading)
    [HttpGet]
    public async Task<ActionResult<MeDto>> Get(CancellationToken ct)
    {
        if (User.LastFmUsername() is { } username)
        {
            var account = await lastFm.GetAccountAsync(User.UserId(), ct);
            return Ok(new MeDto(SessionKinds.LastFm, User.UserId(), username, account.LinkedAt, account.LastPolledAt, null, account.ToDto()));
        }

        if (User.SessionId() is { } sessionId)
        {
            // The cookie was validated a moment ago: ExpiresAt is set (null only if it expired in between)
            var check = await sessions.CheckAsync(sessionId, ct);
            if (check.ExpiresAt is not { } expiresAt)
                throw new CityFyException(ErrorCodes.SessionExpired, "The anonymous session expired", StatusCodes.Status401Unauthorized);
            return Ok(new MeDto(SessionKinds.Anonymous, User.UserId(), null, null, null, expiresAt));
        }

        // Admin session: not a user of the app
        throw new CityFyException(ErrorCodes.Unauthenticated, "No user session", StatusCodes.Status401Unauthorized);
    }

    // DELETE api/v1/me/data — the user deletes everything CityFy keeps about them, then is logged out.
    // USER_DATA_DELETION_INCOMPLETE (502) when a service did not answer: the session stays open so the
    // user can retry (the deletion is idempotent).
    [HttpDelete("data")]
    public async Task<ActionResult<UserDataDeletionResult>> DeleteData(CancellationToken ct)
    {
        var result = await eraser.DeleteAsync(User.UserId(), "user", ct);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(result);
    }
}
