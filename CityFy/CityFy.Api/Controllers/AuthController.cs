using CityFy.Api.Auth;
using CityFy.Api.Clients;
using CityFy.Api.Models;
using CityFy.Api.Options;
using CityFy.Api.RateLimiting;
using CityFy.Api.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ServiceDefault.Errors;

namespace CityFy.Api.Controllers;

/// <summary>
/// Login with Last.fm, as browser navigations (not fetch):
/// GET auth/lastfm/login -> Last.fm authorization -> GET auth/lastfm/callback?token= (the
///   callback of the Last.fm application) -> account cookie -> frontend. The data of an anonymous
///   session open in the same browser moves to the account.
/// Login errors go back to the frontend welcome page as ?error=CODE (ERRORI_UI.md §4).
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "v1")]
[Route("api/v1/auth")]
public sealed class AuthController(
    IAccountService accounts,
    IOptions<FrontendOptions> frontend,
    IWebHostEnvironment environment,
    ILogger<AuthController> logger) : ControllerBase
{
    private static readonly TimeSpan ReturnUrlLifetime = TimeSpan.FromMinutes(10);

    private readonly FrontendOptions _frontend = frontend.Value;

    // GET api/v1/auth/lastfm/login?returnUrl=/cities
    [HttpGet("lastfm/login")]
    [EnableRateLimiting(RateLimitSetup.LastFmLoginPolicy)]
    public async Task<IActionResult> LastFmLogin([FromQuery] string? returnUrl, CancellationToken ct)
    {
        string url;
        try
        {
            url = await accounts.GetLastFmLoginUrlAsync(ct);
        }
        catch (CityFyException ex)
        {
            return RedirectToLoginError(ex.Code);
        }
        RememberReturnUrl(returnUrl);
        return Redirect(url);
    }

    // GET api/v1/auth/lastfm/callback?token=... (the user refused: Last.fm does not come back)
    [HttpGet("lastfm/callback")]
    [EnableRateLimiting(RateLimitSetup.LastFmLoginPolicy)]
    public async Task<IActionResult> LastFmCallback([FromQuery] string? token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token))
            return RedirectToLoginError(ErrorCodes.LastFmAuthFailed);

        AccountLoginResult login;
        try
        {
            // An anonymous session still valid in this browser: its data moves to the account
            login = await accounts.LoginWithLastFmAsync(token, User.SessionId(), ct);
        }
        catch (CityFyException ex)
        {
            logger.LogWarning("Last.fm login failed: {Code} {Detail}", ex.Code, ex.Message);
            return RedirectToLoginError(ex.Code);
        }

        var now = DateTimeOffset.UtcNow;
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            AuthSetup.CreateAccountPrincipal(login.UserId, login.Username),
            new AuthenticationProperties { IsPersistent = true, IssuedUtc = now, ExpiresUtc = now + AuthSetup.AccountCookieLifetime });
        return RedirectToReturnUrl();
    }

    // POST api/v1/auth/logout
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    private void RememberReturnUrl(string? returnUrl) =>
        Response.Cookies.Append(AuthSetup.ReturnUrlCookie, AuthSetup.SafeReturnUrl(returnUrl), new CookieOptions
        {
            HttpOnly = true,
            Secure = !environment.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            MaxAge = ReturnUrlLifetime
        });

    private RedirectResult RedirectToReturnUrl()
    {
        var returnUrl = AuthSetup.SafeReturnUrl(Request.Cookies[AuthSetup.ReturnUrlCookie]);
        Response.Cookies.Delete(AuthSetup.ReturnUrlCookie);
        return Redirect(_frontend.BaseUrl.TrimEnd('/') + returnUrl);
    }

    private RedirectResult RedirectToLoginError(string code) =>
        Redirect($"{_frontend.BaseUrl.TrimEnd('/')}{_frontend.LoginPath}?error={Uri.EscapeDataString(code)}");
}
