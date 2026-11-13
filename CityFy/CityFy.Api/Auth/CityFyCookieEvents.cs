using CityFy.Api.Models;
using CityFy.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.Errors;

namespace CityFy.Api.Auth;

/// <summary>
/// The cookie of an anonymous session only carries its id: on every request the session document
/// decides. An invalid session (expired, deleted) logs the request out and the API answers 401
/// SESSION_EXPIRED; a session due for renewal gets a new cookie with the same lifetime.
/// </summary>
public sealed class CityFyCookieEvents(
    IAnonymousSessionService sessions,
    IAccountService accounts,
    IProblemDetailsService problemDetails) : CookieAuthenticationEvents
{
    private const string SessionExpiredItem = "cityfy.sessionExpired";

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        if (context.Principal?.LastFmUsername() is not null)
        {
            await ValidateAccountAsync(context);
            return;
        }
        if (context.Principal?.SessionId() is not { } sessionId)
        {
            // Neither anonymous nor an account: only the admin panel is left. Anything else (e.g. a cookie
            // of the old Spotify login) would be an owner that never expires: refused.
            if (context.Principal?.IsInRole(AuthSetup.AdminRole) != true)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
            return;
        }

        var check = await sessions.CheckAsync(sessionId, context.HttpContext.RequestAborted);
        switch (check.Outcome)
        {
            case SessionCheckOutcome.Invalid:
                context.HttpContext.Items[SessionExpiredItem] = true;
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                break;
            case SessionCheckOutcome.Renewed:
                // The handler re-issues the cookie as now + (ExpiresUtc - IssuedUtc), i.e. the same lifetime
                context.ShouldRenew = true;
                break;
        }
    }

    /// <summary>
    /// An account whose data was deleted (by the user or an admin) no longer logs in; otherwise the
    /// cookie slides: issued again, with the same lifetime, once it is older than a day.
    /// </summary>
    private async Task ValidateAccountAsync(CookieValidatePrincipalContext context)
    {
        if (!await accounts.ExistsAsync(context.Principal!.UserId(), context.HttpContext.RequestAborted))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }
        if (context.Properties.IssuedUtc is { } issued && DateTimeOffset.UtcNow - issued > AuthSetup.AccountCookieRenewAfter)
            context.ShouldRenew = true;
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (context.HttpContext.Items.ContainsKey(SessionExpiredItem))
            return WriteProblemAsync(context.HttpContext, ErrorCodes.SessionExpired, "The anonymous session expired or its data was deleted");

        // Empty status: UseStatusCodePages turns it into ProblemDetails (401 -> UNAUTHENTICATED)
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    private async Task WriteProblemAsync(HttpContext httpContext, string code, string detail)
    {
        httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = code,
                Detail = detail,
                Extensions = { [ErrorResponses.CodeExtension] = code }
            }
        });
    }
}
