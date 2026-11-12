using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using CityFy.Api.Services;
using ServiceDefault.Errors;

namespace CityFy.Api.Tests;

public sealed class AdminTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _factory.Dispose();

    private static async Task<string?> CodeOf(HttpResponseMessage resp) =>
        JsonDocument.Parse(await resp.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("code").GetString();

    [Fact]
    public async Task Disabled_AdminEndpointsDoNotExist()
    {
        var resp = await _factory.CreateApiClient().PostAsJsonAsync("/api/admin/login", new { username = "admin", password = "test-password" }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task WrongCredentials_AreRejected()
    {
        _factory.AdminEnabled = true;

        var resp = await _factory.CreateApiClient().PostAsJsonAsync("/api/admin/login", new { username = "admin", password = "nope" }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Equal(ErrorCodes.AdminInvalidCredentials, await CodeOf(resp));
    }

    [Fact]
    public async Task WithoutSession_AdminApiIs401()
    {
        _factory.AdminEnabled = true;

        var resp = await _factory.CreateApiClient().GetAsync("/api/admin/pipeline/runs", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Equal(ErrorCodes.Unauthenticated, await CodeOf(resp));
    }

    [Fact]
    public async Task NormalUser_IsForbidden()
    {
        _factory.AdminEnabled = true;
        var client = await _factory.LoginAsync("u1");

        var resp = await client.GetAsync("/api/admin/pipeline/runs", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(resp));
    }

    [Fact]
    public async Task Admin_SeesTheRunsOfEveryUser()
    {
        _factory.AdminEnabled = true;
        _factory.Fakes.OnJson(HttpMethod.Get, "/api/pipeline/runs?limit=50", new[]
        {
            new { runId = "r1", userId = "u1", status = "Done", triggers = Array.Empty<object>(), periods = new[] { "all" }, createdAt = DateTime.UtcNow, dueAt = DateTime.UtcNow },
            new { runId = "r2", userId = "u2", status = "Failed", triggers = Array.Empty<object>(), periods = new[] { "all" }, createdAt = DateTime.UtcNow, dueAt = DateTime.UtcNow },
        });
        var client = await _factory.AdminLoginAsync();

        var runs = await client.GetFromJsonAsync<JsonArray>("/api/admin/pipeline/runs", Ct);

        Assert.Equal(2, runs!.Count);
    }

    [Fact]
    public async Task Admin_CanStartTheMusicBrainzImport_AndSeesTheConflictCode()
    {
        _factory.AdminEnabled = true;
        _factory.Fakes.On(HttpMethod.Post, "/api/retrieve", _ => FakeServices.Problem(HttpStatusCode.Conflict, ErrorCodes.MbRetrieveAlreadyRunning));
        var client = await _factory.AdminLoginAsync();

        var resp = await client.PostAsync("/api/admin/musicbrainz/import", null, Ct);

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        Assert.Equal(ErrorCodes.MbRetrieveAlreadyRunning, await CodeOf(resp));
    }

    [Fact]
    public async Task CancellingAFinishedUpload_IsNotCancellable()
    {
        _factory.AdminEnabled = true;
        _factory.Fakes.On(HttpMethod.Post, "/internal/uploads/up1/cancel", _ => FakeServices.Problem(HttpStatusCode.Conflict, ErrorCodes.ProcessNotCancellable));
        var client = await _factory.AdminLoginAsync();

        var resp = await client.PostAsync("/api/admin/uploads/up1/cancel", null, Ct);

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        Assert.Equal(ErrorCodes.ProcessNotCancellable, await CodeOf(resp));
    }

    [Fact]
    public async Task CancellingARun_IsForwardedToTheOrchestrator()
    {
        _factory.AdminEnabled = true;
        _factory.Fakes.OnJson(HttpMethod.Post, "/api/pipeline/runs/r1/cancel", new { runId = "r1", status = "Cancelled" });
        var client = await _factory.AdminLoginAsync();

        var resp = await client.PostAsync("/api/admin/pipeline/runs/r1/cancel", null, Ct);

        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);
    }

    [Fact]
    public void Redact_HidesSecretLookingFields_Recursively()
    {
        var doc = JsonNode.Parse("""{ "UserId": "u1", "ProtectedRefreshToken": "abc", "nested": [{ "password": "p", "ok": 1 }] }""");

        var redacted = DatabaseBrowser.Redact(doc)!.ToJsonString();

        Assert.DoesNotContain("abc", redacted);
        Assert.DoesNotContain("\"p\"", redacted);
        Assert.Contains("u1", redacted);
        Assert.Contains("\"ok\":1", redacted);
    }
}
