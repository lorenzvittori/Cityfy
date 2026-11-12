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
public sealed class MeController(ISpotifyServiceClient spotify, IUserDataEraser eraser) : ControllerBase
{
    // GET api/v1/me — who is logged in and when Spotify was last synced
    [HttpGet]
    public async Task<ActionResult<MeDto>> Get(CancellationToken ct)
    {
        try
        {
            var account = await spotify.GetAccountAsync(User.UserId(), ct);
            return Ok(new MeDto(account.UserId, account.DisplayName, account.LinkedAt, account.LastRetrieveAt));
        }
        catch (CityFyException ex) when (ex.Code == ErrorCodes.SpotifyAccountNotLinked)
        {
            // Session for an account that no longer exists: close it
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            throw new CityFyException(ErrorCodes.Unauthenticated, "Account no longer linked", StatusCodes.Status401Unauthorized);
        }
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
