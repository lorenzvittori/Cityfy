using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CityFy.Api.Clients;
using CityFy.Api.Consumers;
using CityFy.Api.Repositories;
using ServiceDefault.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CityFy.Api.Tests;

/// <summary>Answers the internal-service calls of the API: (method, path) -> response.</summary>
public sealed class FakeServices : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new();

    public FakeServices On(HttpMethod method, string pathAndQuery, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _routes[$"{method} {pathAndQuery}"] = respond;
        return this;
    }

    public FakeServices OnJson(HttpMethod method, string pathAndQuery, object body, HttpStatusCode status = HttpStatusCode.OK) =>
        On(method, pathAndQuery, _ => Json(body, status));

    public static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK, string mediaType = "application/json") =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, mediaType) };

    public static HttpResponseMessage Problem(HttpStatusCode status, string code) =>
        Json(new { status = (int)status, title = code, code, detail = "downstream detail" }, status, "application/problem+json");

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var key = $"{request.Method} {request.RequestUri!.PathAndQuery}";
        return Task.FromResult(_routes.TryGetValue(key, out var respond)
            ? respond(request)
            : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent($"no fake for {key}") });
    }
}

/// <summary>Records the published messages instead of sending them to RabbitMQ.</summary>
public sealed class RecordingPublisher : IMessagePublisher
{
    public System.Collections.Concurrent.ConcurrentQueue<object> Messages { get; } = new();

    public Task PublishAsync<T>(T message, string? correlationId = null, CancellationToken ct = default) where T : class
    {
        Messages.Enqueue(message);
        return Task.CompletedTask;
    }
}

/// <summary>Gives every test request the loopback address, before the rest of the pipeline.</summary>
internal sealed class LoopbackClientStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((ctx, nextMiddleware) =>
        {
            ctx.Connection.RemoteIpAddress ??= System.Net.IPAddress.Loopback;
            return nextMiddleware(ctx);
        });
        next(app);
    };
}

/// <summary>The real API pipeline, with the internal services replaced by <see cref="FakeServices"/>.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public FakeServices Fakes { get; } = new();

    /// <summary>'anonymous_sessions' (the tests never open Mongo).</summary>
    public InMemoryAnonymousSessionRepository Sessions { get; } = new();

    /// <summary>'users' (CityFy accounts).</summary>
    public InMemoryUserRepository Users { get; } = new();

    /// <summary>Messages the gateway published (UserActive).</summary>
    public RecordingPublisher Published { get; } = new();

    /// <summary>Admin panel switch (off by default, as in production). Set before the first client.</summary>
    public bool AdminEnabled { get; set; }

    /// <summary>
    /// "RateLimits" settings (key → value). High by default so the other tests never hit them; the
    /// rate-limit tests lower them. Set before the first client.
    /// </summary>
    public Dictionary<string, string> RateLimits { get; } = new()
    {
        ["SessionStartsPerHour"] = "10000",
        ["UploadsPerHour"] = "10000",
        ["UploadsPerUserPerDay"] = "10000",
        ["ConcurrentUploads"] = "1000",
        ["LastFmLoginsPerHour"] = "10000",
        ["ApiRequestsPerMinute"] = "100000",
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("Frontend:BaseUrl", "http://frontend.test");
        builder.UseSetting("Admin:Enabled", AdminEnabled ? "true" : "false");
        builder.UseSetting("Admin:Username", "admin");
        builder.UseSetting("Admin:Password", "test-password");

        // The database clients need a connection string to be registered (never opened by these tests)
        builder.UseSetting("ConnectionStrings:cityfy", "mongodb://localhost:27017/cityfy");
        builder.UseSetting("ConnectionStrings:musicbrainz", "Server=localhost;Database=musicbrainz;User Id=sa;Password=x;TrustServerCertificate=true");

        // Service discovery resolves the resource names to these addresses
        foreach (var resource in new[] { "Catalog", "CityGenerator", "Orchestrator", "RetrieveDumpSpotify", "RetrieveLastFm", "RetrieveMusicBrainz" })
            builder.UseSetting($"services:{resource}:https:0", $"https://{resource.ToLowerInvariant()}.test");

        // The cleanup is run by the tests themselves (IAnonymousSessionService.CleanupExpiredAsync)
        builder.UseSetting("Sessions:CleanupIntervalMinutes", "0");
        // RabbitMQ is never opened: the publisher is faked and the consumer removed
        builder.UseSetting("ConnectionStrings:broker", "amqp://guest:guest@localhost:5672");
        builder.UseSetting("Aspire:RabbitMQ:Client:DisableHealthChecks", "true");
        foreach (var (key, value) in RateLimits)
            builder.UseSetting($"RateLimits:{key}", value);

        builder.ConfigureTestServices(services =>
        {
            // The test server has no client address: requests come from loopback, a trusted proxy,
            // so a test can choose its network with X-Forwarded-For
            services.AddTransient<IStartupFilter, LoopbackClientStartupFilter>();
            services.AddSingleton<IAnonymousSessionRepository>(Sessions);
            services.AddSingleton<IMessagePublisher>(Published);
            foreach (var consumer in services.Where(d => d.ImplementationType == typeof(NowPlayingChangedConsumer)).ToList())
                services.Remove(consumer);
            services.AddSingleton<IUserRepository>(Users);
            services.AddHttpClient<ICatalogClient, CatalogClient>().ConfigurePrimaryHttpMessageHandler(() => Fakes);
            services.AddHttpClient<ICityGeneratorClient, CityGeneratorClient>().ConfigurePrimaryHttpMessageHandler(() => Fakes);
            services.AddHttpClient<IOrchestratorClient, OrchestratorClient>().ConfigurePrimaryHttpMessageHandler(() => Fakes);
            services.AddHttpClient<ILastFmServiceClient, LastFmServiceClient>().ConfigurePrimaryHttpMessageHandler(() => Fakes);
            services.AddHttpClient<IDumpClient, DumpClient>().ConfigurePrimaryHttpMessageHandler(() => Fakes);
            services.AddHttpClient<IMusicBrainzServiceClient, MusicBrainzServiceClient>().ConfigurePrimaryHttpMessageHandler(() => Fakes);
        });
    }

    /// <summary>Logs in to the admin panel with the mock admin user.</summary>
    public async Task<HttpClient> AdminLoginAsync()
    {
        var client = CreateApiClient();
        var resp = await client.PostAsJsonAsync("/api/admin/login", new { username = "admin", password = "test-password" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return client;
    }

    /// <summary>Client on https (the session cookie is Secure), redirects not followed.</summary>
    public HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });

    /// <summary>Starts an anonymous session through the real endpoint; returns the client and the session id.</summary>
    public async Task<(HttpClient Client, string SessionId)> StartAnonymousAsync()
    {
        var before = Sessions.Sessions.Keys.ToHashSet();
        var client = CreateApiClient();
        var resp = await client.PostAsync("/api/v1/session", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (client, Assert.Single(Sessions.Sessions.Keys, k => !before.Contains(k)));
    }

    /// <summary>
    /// Fake Last.fm link: a Last.fm account seen for the first time takes the id proposed by the
    /// gateway; <paramref name="existingUserId"/> simulates an account already linked.
    /// </summary>
    public void FakeLastFmLink(string username = "alice", string? existingUserId = null) =>
        Fakes.On(HttpMethod.Post, "/internal/lastfm/link", req =>
        {
            var body = System.Text.Json.JsonDocument.Parse(req.Content!.ReadAsStringAsync().Result).RootElement;
            var userId = existingUserId ?? body.GetProperty("userId").GetString()!;
            Fakes.OnJson(HttpMethod.Get, $"/internal/lastfm/accounts/{userId}", new
            {
                userId, username, status = "Active", linkedAt = DateTime.UtcNow, lastScrobbleAt = DateTime.UtcNow,
                nextPollAt = DateTime.UtcNow, historyImportId = "i1"
            });
            return FakeServices.Json(new { userId, username, importId = existingUserId is null ? "i1" : null });
        });

    /// <summary>Logs in with Last.fm through the real callback (on <paramref name="client"/>, e.g. with an anonymous session); returns the response.</summary>
    public async Task<HttpResponseMessage> LastFmCallbackAsync(HttpClient client, string token = "token") =>
        await client.GetAsync($"/api/v1/auth/lastfm/callback?token={token}", TestContext.Current.CancellationToken);

    /// <summary>
    /// Logs in as the account <paramref name="userId"/> through the real Last.fm callback (its Last.fm
    /// account already linked to that id, so the owner keeps the id the test chose).
    /// </summary>
    public async Task<HttpClient> LoginAsync(string userId)
    {
        FakeLastFmLink(username: $"lastfm-{userId}", existingUserId: userId);
        var client = CreateApiClient();
        var resp = await LastFmCallbackAsync(client);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        return client;
    }
}
