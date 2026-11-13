using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using CityFy.Api.Auth;
using CityFy.Api.Models;
using CityFy.Api.Options;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ServiceDefault.Errors;

namespace CityFy.Api.RateLimiting;

/// <summary>
/// Limits of the public API per client network (<see cref="ClientNetwork"/>), see <see cref="RateLimitOptions"/>:
/// - endpoint policies: "Inizia senza account", uploads and Last.fm login per network and hour;
/// - global: every api/v1 request per network and minute; uploads per user and day, and uploads in
///   progress on the whole server.
/// A rejected request answers 429 RATE_LIMITED (+ Retry-After when known); a rejected login, being a
/// browser navigation, goes back to the welcome page with ?error=RATE_LIMITED.
/// </summary>
public static class RateLimitSetup
{
    public const string SessionStartPolicy = "session-start";
    public const string UploadPolicy = "uploads";
    public const string LastFmLoginPolicy = "lastfm-login";

    public const string RetryAfterExtension = "retryAfterSeconds";

    private const string PublicApiPrefix = "/api/v1";
    private const string UploadsPath = "/api/v1/uploads";
    private const string LoginPrefix = "/api/v1/auth";
    private const string NoLimit = "none";

    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddCityFyRateLimits(this IServiceCollection services)
    {
        services.AddOptions<RateLimitOptions>()
            .BindConfiguration(RateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<ProxyOptions>().BindConfiguration(ProxyOptions.SectionName);

        // The client address is the one the reverse proxy saw (X-Forwarded-For), only from trusted proxies
        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<ProxyOptions>>((o, proxy) =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            foreach (var network in proxy.Value.KnownNetworks)
                o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        });

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = OnRejectedAsync;

            o.AddPolicy(SessionStartPolicy, ctx => PerNetwork(ctx, l => l.SessionStartsPerHour, Hour));
            o.AddPolicy(UploadPolicy, ctx => PerNetwork(ctx, l => l.UploadsPerHour, Hour));
            o.AddPolicy(LastFmLoginPolicy, ctx => PerNetwork(ctx, l => l.LastFmLoginsPerHour, Hour));

            o.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(ctx => IsPublicApi(ctx)
                    ? PerNetwork(ctx, l => l.ApiRequestsPerMinute, Minute)
                    : RateLimitPartition.GetNoLimiter(NoLimit)),
                PartitionedRateLimiter.Create<HttpContext, string>(ctx => IsUpload(ctx)
                    ? RateLimitPartition.GetConcurrencyLimiter("uploads-in-progress", _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = Limits(ctx).ConcurrentUploads,
                        QueueLimit = 0
                    })
                    : RateLimitPartition.GetNoLimiter(NoLimit)),
                PartitionedRateLimiter.Create<HttpContext, string>(ctx => IsUpload(ctx) && ctx.User.Identity?.IsAuthenticated == true
                    ? FixedWindow($"uploads-user:{ctx.User.UserId()}", Limits(ctx).UploadsPerUserPerDay, Day)
                    : RateLimitPartition.GetNoLimiter(NoLimit)));
        });
        return services;
    }

    private static RateLimitOptions Limits(HttpContext ctx) => ctx.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;

    private static RateLimitPartition<string> PerNetwork(HttpContext ctx, Func<RateLimitOptions, int> limit, TimeSpan window) =>
        FixedWindow(ClientNetwork.From(ctx.Connection.RemoteIpAddress).Key, limit(Limits(ctx)), window);

    private static RateLimitPartition<string> FixedWindow(string key, int permits, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = window,
            QueueLimit = 0
        });

    private static bool IsPublicApi(HttpContext ctx) => ctx.Request.Path.StartsWithSegments(PublicApiPrefix);

    private static bool IsUpload(HttpContext ctx) => HttpMethods.IsPost(ctx.Request.Method) && ctx.Request.Path.Equals(UploadsPath);

    private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken ct)
    {
        var http = context.HttpContext;
        int? retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
            ? (int)Math.Ceiling(wait.TotalSeconds)
            : null;

        if (http.Request.Path.StartsWithSegments(LoginPrefix))
        {
            var frontend = http.RequestServices.GetRequiredService<IOptions<FrontendOptions>>().Value;
            http.Response.Redirect($"{frontend.BaseUrl.TrimEnd('/')}{frontend.LoginPath}?error={ErrorCodes.RateLimited}");
            return;
        }

        if (retryAfter is { } seconds)
            http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = ErrorCodes.RateLimited,
            Detail = "Too many requests from this network",
            Extensions = { [ErrorResponses.CodeExtension] = ErrorCodes.RateLimited }
        };
        if (retryAfter is not null)
            problem.Extensions[RetryAfterExtension] = retryAfter;
        await http.RequestServices.GetRequiredService<IProblemDetailsService>()
            .WriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = problem });
    }
}
