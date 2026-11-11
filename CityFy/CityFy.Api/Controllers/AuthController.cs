using CityFy.Api.Auth;
using CityFy.Api.Clients;
using CityFy.Api.Models;
using CityFy.Api.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ServiceDefault.Errors;

namespace CityFy.Api.Controllers;

/// <summary>
/// Spotify login. The browser goes: GET auth/login -> Spotify consent -> GET auth/callback (this API,
/// it is the redirect URI registered on the Spotify dashboard) -> session cookie -> frontend.
/// Login errors go back to the frontend login page as ?error=CODE (ERRORI_UI.md §4).
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "v1")]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly SpotifyServiceClient _spotify;
    private readonly FrontendOptions _frontend;
    private readonly ILogger<AuthController> _logger;
    private readonly IWebHostEnvironment _environment;

    public AuthController(SpotifyServiceClient spotify, IOptions<FrontendOptions> frontend, ILogger<AuthController> logger, IWebHostEnvironment environment)
    {
        _environment = environment;
        _spotify = spotify;
        _frontend = frontend.Value;
        _logger = logger;
    }

    // GET api/v1/auth/login?returnUrl=/city — browser navigation (not fetch)
    [HttpGet("login")]
    public async Task<IActionResult> Login([FromQuery] string? returnUrl, CancellationToken ct)
    {
        var url = await _spotify.GetAuthorizeUrlAsync(ct);

        Response.Cookies.Append(AuthSetup.ReturnUrlCookie, AuthSetup.SafeReturnUrl(returnUrl), new CookieOptions
        {
            HttpOnly = true,
            Secure = !_environment.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(10)
        });
        return Redirect(url);
    }

    // GET api/v1/auth/callback?code=...&state=... (or ?error=access_denied)
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
            return RedirectToLoginError(ErrorCodes.SpotifyAuthDenied);

        SpotifyLinkResult linked;
        try
        {
            linked = await _spotify.LinkAsync(code, state, ct);
        }
        catch (CityFyException ex)
        {
            _logger.LogWarning("Spotify login failed: {Code} {Detail}", ex.Code, ex.Message);
            return RedirectToLoginError(ex.Code);
        }

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            AuthSetup.CreatePrincipal(linked.UserId, linked.DisplayName),
            new AuthenticationProperties { IsPersistent = true });

        var returnUrl = AuthSetup.SafeReturnUrl(Request.Cookies[AuthSetup.ReturnUrlCookie]);
        Response.Cookies.Delete(AuthSetup.ReturnUrlCookie);
        return Redirect(_frontend.BaseUrl.TrimEnd('/') + returnUrl);
    }

    // POST api/v1/auth/logout
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    private RedirectResult RedirectToLoginError(string code) =>
        Redirect($"{_frontend.BaseUrl.TrimEnd('/')}{_frontend.LoginPath}?error={Uri.EscapeDataString(code)}");
}

[ApiController]
[Authorize]
[ApiExplorerSettings(GroupName = "v1")]
[Route("api/v1/me")]
public class MeController : ControllerBase
{
    // GET api/v1/me — who is logged in and when Spotify was last synced
    [HttpGet]
    public async Task<ActionResult<MeDto>> Get([FromServices] SpotifyServiceClient spotify, CancellationToken ct)
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

    // DELETE api/v1/me/data — the user deletes everything Cityfy keeps about them, then is logged out.
    // USER_DATA_DELETION_INCOMPLETE (502) when a service did not answer: the session stays open so the
    // user can retry (the deletion is idempotent).
    [HttpDelete("data")]
    public async Task<IActionResult> DeleteData([FromServices] CityFy.Api.Account.UserDataEraser eraser, CancellationToken ct)
    {
        var result = await eraser.DeleteAsync(User.UserId(), "user", ct);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(result);
    }
}
