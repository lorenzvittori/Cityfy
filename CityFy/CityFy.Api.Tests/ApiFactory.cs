using System.Net;
using System.Text;
using System.Text.Json;
using CityFy.Api.Clients;
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

/// <summary>The real API pipeline, with the internal services replaced by <see cref="FakeServices"/>.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public FakeServices Fakes { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("Frontend:BaseUrl", "http://frontend.test");

        // Service discovery resolves the resource names to these addresses
        foreach (var resource in new[] { "Elaboration", "Orchestrator", "RetrieveSpotify", "RetrieveDumpSpotify" })
            builder.UseSetting($"services:{resource}:https:0", $"https://{resource.ToLowerInvariant()}.test");

        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient<ElaborationClient>().ConfigurePrimaryHttpMessageHandler(() => Fakes);
            services.AddHttpClient<OrchestratorClient>().ConfigurePrimaryHttpMessageHandler(() => Fakes);
            services.AddHttpClient<SpotifyServiceClient>().ConfigurePrimaryHttpMessageHandler(() => Fakes);
            services.AddHttpClient<DumpClient>().ConfigurePrimaryHttpMessageHandler(() => Fakes);
        });
    }

    /// <summary>Client on https (the session cookie is Secure), redirects not followed.</summary>
    public HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });

    /// <summary>Logs in as <paramref name="userId"/> through the real callback (fake Spotify link).</summary>
    public async Task<HttpClient> LoginAsync(string userId)
    {
        Fakes.OnJson(HttpMethod.Post, "/internal/spotify/link", new { userId, displayName = "Test User" });
        var client = CreateApiClient();
        var resp = await client.GetAsync("/api/v1/auth/callback?code=c&state=s", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        return client;
    }
}
