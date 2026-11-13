using CityFy.Api.Auth;
using CityFy.Api.Models;
using CityFy.Api.RateLimiting;
using CityFy.Api.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace CityFy.Api.Controllers;

/// <summary>
/// Start without an account. The session is created only when the visitor chooses to start (not on a
/// simple visit), lives Sessions:Lifetime after the last visit, then its data is deleted.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "v1")]
[Route("api/v1/session")]
public sealed class SessionController(IAnonymousSessionService sessions) : ControllerBase
{
    // POST api/v1/session — idempotent: a request that already has a valid session gets it back
    [HttpPost]
    [EnableRateLimiting(RateLimitSetup.SessionStartPolicy)]
    public async Task<ActionResult<SessionDto>> Start(CancellationToken ct)
    {
        // Logged in with Last.fm: nothing to start, the account stays
        if (User.LastFmUsername() is not null)
            return Ok(new SessionDto(SessionKinds.LastFm, null));
        if (User.SessionId() is { } current && (await sessions.CheckAsync(current, ct)).ExpiresAt is { } expiresAt)
            return Ok(new SessionDto(SessionKinds.Anonymous, expiresAt));

        var session = await sessions.StartAsync(ct);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            AuthSetup.CreateAnonymousPrincipal(session.Id),
            // Issued/Expires span = Lifetime: every renewal re-issues the cookie with the same span
            new AuthenticationProperties
            {
                IsPersistent = true,
                IssuedUtc = session.CreatedAt,
                ExpiresUtc = session.ExpiresAt
            });
        return Ok(new SessionDto(SessionKinds.Anonymous, session.ExpiresAt));
    }
}
