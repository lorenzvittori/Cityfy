using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using ServiceDefault.Errors;

namespace CityFy.Api.Auth;

public static class AuthSetup
{
    public const string ReturnUrlCookie = "cityfy.returnUrl";

    /// <summary>
    /// Session cookie created after the Spotify login (the Spotify user id is the CityFy user id).
    /// HttpOnly + SameSite=Lax: the frontend calls the API through its own origin (dev-server proxy).
    /// Unauthenticated API calls answer 401 UNAUTHENTICATED instead of redirecting.
    /// </summary>
    public static IServiceCollection AddCityFyAuth(this IServiceCollection services, IHostEnvironment environment)
    {
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
                o.SlidingExpiration = true;
                o.Events.OnRedirectToLogin = ctx => WriteStatus(ctx.Response, StatusCodes.Status401Unauthorized);
                o.Events.OnRedirectToAccessDenied = ctx => WriteStatus(ctx.Response, StatusCodes.Status403Forbidden);
            });
        services.AddAuthorization(o => o.AddPolicy(AdminPolicy, p => p.RequireRole(AdminRole)));
        return services;
    }

    public const string AdminRole = "Admin";
    public const string AdminPolicy = "Admin";

    public static ClaimsPrincipal CreateAdminPrincipal(string username) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, $"admin:{username}"),
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, AdminRole)
        ], CookieAuthenticationDefaults.AuthenticationScheme));

    // Empty status: UseStatusCodePages turns it into ProblemDetails (401 -> UNAUTHENTICATED)
    private static Task WriteStatus(HttpResponse response, int status)
    {
        response.StatusCode = status;
        return Task.CompletedTask;
    }

    public static ClaimsPrincipal CreatePrincipal(string userId, string? displayName) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, displayName ?? userId)
        ], CookieAuthenticationDefaults.AuthenticationScheme));

    /// <summary>Spotify user id of the session (endpoints are [Authorize], so it is always present).</summary>
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
