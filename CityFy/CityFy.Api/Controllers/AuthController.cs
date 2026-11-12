using CityFy.Api.Auth;
using CityFy.Api.Clients;
using CityFy.Api.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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
public sealed class AuthController(
    ISpotifyServiceClient spotify,
    IOptions<FrontendOptions> frontend,
    IWebHostEnvironment environment,
    ILogger<AuthController> logger) : ControllerBase
{
    private static readonly TimeSpan ReturnUrlLifetime = TimeSpan.FromMinutes(10);

    private readonly FrontendOptions _frontend = frontend.Value;

    // GET api/v1/auth/login?returnUrl=/city — browser navigation (not fetch)
    [HttpGet("login")]
    public async Task<IActionResult> Login([FromQuery] string? returnUrl, CancellationToken ct)
    {
        var url = await spotify.GetAuthorizeUrlAsync(ct);

        Response.Cookies.Append(AuthSetup.ReturnUrlCookie, AuthSetup.SafeReturnUrl(returnUrl), new CookieOptions
        {
            HttpOnly = true,
            Secure = !environment.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            MaxAge = ReturnUrlLifetime
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
            linked = await spotify.LinkAsync(code, state, ct);
        }
        catch (CityFyException ex)
        {
            logger.LogWarning("Spotify login failed: {Code} {Detail}", ex.Code, ex.Message);
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
