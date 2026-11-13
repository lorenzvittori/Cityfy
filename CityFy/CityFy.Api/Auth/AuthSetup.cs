using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using ServiceDefault.Errors;

namespace CityFy.Api.Auth;

public static class AuthSetup
{
    public const string ReturnUrlCookie = "cityfy.returnUrl";

    /// <summary>Claim with the id of an anonymous session (absent for the other kinds of session).</summary>
    public const string SessionIdClaim = "cityfy:session";

    /// <summary>Prefix of the owner id of an anonymous session, as the internal services receive it.</summary>
    public const string AnonymousUserIdPrefix = "anon_";

    /// <summary>Claim present on the session of a CityFy account (login with Last.fm): its user name.</summary>
    public const string LastFmUsernameClaim = "cityfy:lastfm";

    /// <summary>Prefix of the id of a CityFy account ('users').</summary>
    public const string AccountUserIdPrefix = "u_";

    /// <summary>Cookie of an account: valid this long after the last renewal.</summary>
    public static readonly TimeSpan AccountCookieLifetime = TimeSpan.FromDays(30);

    /// <summary>An account cookie older than this is issued again (sliding, at most once a day).</summary>
    public static readonly TimeSpan AccountCookieRenewAfter = TimeSpan.FromDays(1);

    /// <summary>
    /// Session cookie: an anonymous session (no account), a CityFy account (login with Last.fm) or the
    /// admin panel. HttpOnly + SameSite=Lax: the frontend calls the API through its own origin
    /// (dev-server proxy). Unauthenticated API calls answer 401 UNAUTHENTICATED (SESSION_EXPIRED for an
    /// anonymous session that is no longer valid) instead of redirecting; see <see cref="CityFyCookieEvents"/>.
    /// </summary>
    public static IServiceCollection AddCityFyAuth(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddScoped<CityFyCookieEvents>();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = "cityfy.auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                // Development: the browser talks http://127.0.0.1:4200 (dev-server proxy), where a Secure
                // cookie is not reliably accepted. Everywhere else the cookie is HTTPS-only.
                o.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.None : CookieSecurePolicy.Always;
                o.ExpireTimeSpan = TimeSpan.FromDays(14);
                // No automatic sliding: an anonymous cookie must not outlive its session (14 days). Anonymous
                // sessions are renewed by CityFyCookieEvents, on the server-side expiry.
                o.SlidingExpiration = false;
                o.EventsType = typeof(CityFyCookieEvents);
            });
        services.AddAuthorization(o => o.AddPolicy(AdminPolicy, p => p.RequireRole(AdminRole)));
        return services;
    }

    public const string AdminRole = "Admin";
    public const string AdminPolicy = "Admin";

    public static string AnonymousUserId(string sessionId) => AnonymousUserIdPrefix + sessionId;

    /// <summary>Session id of an owner id of an anonymous session ("anon_{id}"), otherwise null.</summary>
    public static string? SessionIdOf(string userId) =>
        userId.StartsWith(AnonymousUserIdPrefix, StringComparison.Ordinal) ? userId[AnonymousUserIdPrefix.Length..] : null;

    public static ClaimsPrincipal CreateAnonymousPrincipal(string sessionId) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, AnonymousUserId(sessionId)),
            new Claim(SessionIdClaim, sessionId)
        ], CookieAuthenticationDefaults.AuthenticationScheme));

    /// <summary>Id of the anonymous session of the request, null for the other kinds of session.</summary>
    public static string? SessionId(this ClaimsPrincipal user) => user.FindFirstValue(SessionIdClaim);

    public static ClaimsPrincipal CreateAccountPrincipal(string userId, string lastFmUsername) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, lastFmUsername),
            new Claim(LastFmUsernameClaim, lastFmUsername)
        ], CookieAuthenticationDefaults.AuthenticationScheme));

    /// <summary>Last.fm user name of a CityFy account session, null for the other kinds of session.</summary>
    public static string? LastFmUsername(this ClaimsPrincipal user) => user.FindFirstValue(LastFmUsernameClaim);

    public static ClaimsPrincipal CreateAdminPrincipal(string username) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, $"admin:{username}"),
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, AdminRole)
        ], CookieAuthenticationDefaults.AuthenticationScheme));

    /// <summary>Owner id of the session: "anon_{sessionId}" or "u_…" of an account (endpoints are [Authorize], so it is always present).</summary>
    public static string UserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new CityFyException(ErrorCodes.Unauthenticated, "No user in session", StatusCodes.Status401Unauthorized);

    /// <summary>Only local paths ("/city?x=1"): avoids open redirects to other sites.</summary>
    public static string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) &&
        returnUrl.StartsWith('/') &&
        !returnUrl.StartsWith("//") &&
        !returnUrl.StartsWith("/\\") &&
        !returnUrl.Contains("://")
            ? returnUrl
            : "/";
}
