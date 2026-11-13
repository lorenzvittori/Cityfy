using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CityFy.Api.Models;
using CityFy.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using ServiceDefault.Errors;
using ServiceDefault.Models;

namespace CityFy.Api.Tests;

/// <summary>"Accedi con Last.fm": accounts, cookie, and the move of the data of an anonymous session.</summary>
public sealed class AccountTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _factory.Dispose();

    private string OnlyUserId => Assert.Single(_factory.Users.Users.Keys);

    /// <summary>The services that take part in a transfer answer; returns the calls ("METHOD path").</summary>
    private List<string> TransferServices(string fromUserId, bool uploadsComplete = true, long plays = 3)
    {
        var calls = new List<string>();
        void Record(HttpMethod method, string path, Func<HttpResponseMessage> respond) =>
            _factory.Fakes.On(method, path, _ => { calls.Add($"{method} {path}"); return respond(); });

        // Dump and Catalog share the path: one answer valid for both
        Record(HttpMethod.Post, $"/internal/users/{fromUserId}/transfer", () =>
            FakeServices.Json(new UserDataTransfer("svc", new Dictionary<string, long> { ["plays"] = plays, ["uploads"] = 1 }, uploadsComplete)));
        Record(HttpMethod.Delete, $"/internal/users/{fromUserId}/data", () =>
            FakeServices.Json(new UserDataDeletion("svc", new Dictionary<string, long>())));
        _factory.Fakes.On(HttpMethod.Post, "/internal/pipeline/trigger/", _ => FakeServices.Json(new { }));
        return calls;
    }

    private void TriggerAnswers(List<string> calls, string userId) =>
        _factory.Fakes.On(HttpMethod.Post, $"/internal/pipeline/trigger/{userId}", _ =>
        {
            calls.Add($"POST trigger {userId}");
            return FakeServices.Json(new { runId = "r1", userId, status = "Pending", triggers = Array.Empty<object>(), createdAt = DateTime.UtcNow, dueAt = DateTime.UtcNow });
        });

    // --- login ------------------------------------------------------------------------------

    [Fact]
    public async Task LastFmLogin_RedirectsToTheAuthorizationOfLastFm()
    {
        _factory.Fakes.OnJson(HttpMethod.Get, "/internal/lastfm/authorize-url", new { url = "https://www.last.fm/api/auth/?api_key=k" });

        var resp = await _factory.CreateApiClient().GetAsync("/api/v1/auth/lastfm/login?returnUrl=/cities", Ct);

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal("https://www.last.fm/api/auth/?api_key=k", resp.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Callback_NewLastFmAccount_CreatesTheUser_AndOpensAnAccountSession()
    {
        _factory.FakeLastFmLink();
        var client = _factory.CreateApiClient();

        var resp = await _factory.LastFmCallbackAsync(client);

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.StartsWith("http://frontend.test/", resp.Headers.Location!.ToString());
        Assert.StartsWith("u_", OnlyUserId);
        Assert.Equal("alice", _factory.Users.Users[OnlyUserId].LastFmUsername);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me", Ct);
        Assert.Equal("lastfm", me.GetProperty("kind").GetString());
        Assert.Equal(OnlyUserId, me.GetProperty("userId").GetString());
        Assert.Equal("alice", me.GetProperty("displayName").GetString());
        Assert.Equal("https://www.last.fm/user/alice", me.GetProperty("lastFm").GetProperty("profileUrl").GetString());
        Assert.Equal(JsonValueKind.Null, me.GetProperty("expiresAt").ValueKind);
    }

    [Fact]
    public async Task Callback_LastFmAccountAlreadyKnown_LogsInTheSameUser()
    {
        await _factory.Users.RecordLoginAsync("u_existing", "alice", DateTime.UtcNow.AddDays(-3), Ct);
        _factory.FakeLastFmLink(existingUserId: "u_existing");
        var client = _factory.CreateApiClient();

        await _factory.LastFmCallbackAsync(client);

        Assert.Equal("u_existing", OnlyUserId);
        Assert.Equal("u_existing", (await client.GetFromJsonAsync<JsonElement>("/api/v1/me", Ct)).GetProperty("userId").GetString());
    }

    [Theory]
    [InlineData("?token=bad")]
    [InlineData("")]
    public async Task Callback_TokenRefusedOrMissing_GoesBackToTheWelcomePageWithTheCode(string query)
    {
        _factory.Fakes.On(HttpMethod.Post, "/internal/lastfm/link", _ => FakeServices.Problem(HttpStatusCode.BadRequest, ErrorCodes.LastFmAuthFailed));

        var resp = await _factory.CreateApiClient().GetAsync($"/api/v1/auth/lastfm/callback{query}", Ct);

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal("http://frontend.test/welcome?error=LASTFM_AUTH_FAILED", resp.Headers.Location!.ToString());
        Assert.Empty(_factory.Users.Users);
    }

    [Fact]
    public async Task AccountDeleted_TheCookieNoLongerWorks()
    {
        _factory.FakeLastFmLink();
        var client = _factory.CreateApiClient();
        await _factory.LastFmCallbackAsync(client);
        _factory.Users.Users.Clear();

        var resp = await client.GetAsync("/api/v1/me", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteMyData_AsAnAccount_DeletesTheUser()
    {
        _factory.FakeLastFmLink();
        var client = _factory.CreateApiClient();
        await _factory.LastFmCallbackAsync(client);
        var userId = OnlyUserId;
        foreach (var path in new[] { $"/internal/lastfm/users/{userId}/data", $"/internal/users/{userId}/data" })
            _factory.Fakes.OnJson(HttpMethod.Delete, path, new UserDataDeletion("svc", new Dictionary<string, long>()));

        var resp = await client.DeleteAsync("/api/v1/me/data", Ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Empty(_factory.Users.Users);
    }

    [Fact]
    public async Task LastFmImport_OfAnotherUser_IsNotFound()
    {
        _factory.FakeLastFmLink();
        var client = _factory.CreateApiClient();
        await _factory.LastFmCallbackAsync(client);
        _factory.Fakes.OnJson(HttpMethod.Get, "/internal/lastfm/imports/i1",
            new { importId = "i1", userId = OnlyUserId, status = "InProgress", inserted = 400, startedAt = DateTime.UtcNow, progressPercent = 25.0, progressDetail = "Page 1 of 4" });
        _factory.Fakes.OnJson(HttpMethod.Get, "/internal/lastfm/imports/i2",
            new { importId = "i2", userId = "u_other", status = "Done", inserted = 1, startedAt = DateTime.UtcNow });

        var mine = await client.GetFromJsonAsync<JsonElement>("/api/v1/lastfm/imports/i1", Ct);
        var other = await client.GetAsync("/api/v1/lastfm/imports/i2", Ct);

        Assert.Equal(25.0, mine.GetProperty("progressPercent").GetDouble());
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
    }

    [Fact]
    public async Task Me_ShowsTheHintWhenNoPlayArrivesForDays()
    {
        _factory.FakeLastFmLink();
        var client = _factory.CreateApiClient();
        await _factory.LastFmCallbackAsync(client);
        _factory.Fakes.OnJson(HttpMethod.Get, $"/internal/lastfm/accounts/{OnlyUserId}", new
        {
            userId = OnlyUserId, username = "alice", status = "Active", linkedAt = DateTime.UtcNow.AddDays(-9),
            lastScrobbleAt = DateTime.UtcNow.AddDays(-5), nextPollAt = DateTime.UtcNow, warning = ErrorCodes.LastFmSpotifyNotScrobbling
        });

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me", Ct);

        Assert.Equal(ErrorCodes.LastFmSpotifyNotScrobbling, me.GetProperty("lastFm").GetProperty("warning").GetString());
    }

    // --- end of the Last.fm licence ---------------------------------------------------------

    [Fact]
    public async Task Purge_NeedsTheConfirmationPhrase()
    {
        _factory.AdminEnabled = true;
        var admin = await _factory.AdminLoginAsync();

        var resp = await admin.PostAsJsonAsync("/api/admin/lastfm/purge", new { confirm = "yes" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Purge_DeletesRetrieveLastFm_ThenEveryAccountOnEveryService()
    {
        _factory.AdminEnabled = true;
        await _factory.Users.RecordLoginAsync("u_a", "alice", DateTime.UtcNow, Ct);
        await _factory.Users.RecordLoginAsync("u_b", "bob", DateTime.UtcNow, Ct);
        var calls = new List<string>();
        _factory.Fakes.On(HttpMethod.Delete, "/internal/lastfm/data", _ =>
        {
            calls.Add("lastfm-all");
            return FakeServices.Json(new UserDataDeletion("RetrieveLastFm", new Dictionary<string, long> { ["lastfmAccounts"] = 2 }));
        });
        foreach (var user in new[] { "u_a", "u_b" })
            foreach (var path in new[] { $"/internal/lastfm/users/{user}/data", $"/internal/users/{user}/data" })
                _factory.Fakes.On(HttpMethod.Delete, path, _ =>
                {
                    calls.Add(user);
                    return FakeServices.Json(new UserDataDeletion("svc", new Dictionary<string, long>()));
                });
        var admin = await _factory.AdminLoginAsync();

        var resp = await admin.PostAsJsonAsync("/api/admin/lastfm/purge", new { confirm = "DELETE ALL LASTFM DATA" }, Ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(2, (await resp.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("deletedAccounts").GetInt32());
        Assert.Equal("lastfm-all", calls[0]); // polling and imports stopped first
        Assert.Equal(5, calls.Count(c => c == "u_a")); // Retrieve.LastFm, Dump, Orchestrator, CityGenerator, Catalog
        Assert.Empty(_factory.Users.Users);
    }

    // --- move of the data of an anonymous session (L8) ------------------------------------

    [Fact]
    public async Task Login_FromAnAnonymousSession_MovesItsDataToTheAccount()
    {
        var (client, sessionId) = await _factory.StartAnonymousAsync();
        var anon = $"anon_{sessionId}";
        var calls = TransferServices(anon);
        _factory.FakeLastFmLink();
        _factory.Fakes.On(HttpMethod.Post, "/internal/lastfm/link", WrapLinkToRecordTrigger(calls));

        await _factory.LastFmCallbackAsync(client);

        var session = _factory.Sessions.Sessions[sessionId];
        Assert.Equal(AnonymousSessionStatus.Transferred, session.Status);
        Assert.Equal(OnlyUserId, session.TransferTo);
        // uploads + plays move, derived data of the anonymous owner is deleted, the account's catalog is rebuilt
        Assert.Equal(
            [$"POST /internal/users/{anon}/transfer", $"POST /internal/users/{anon}/transfer",
             $"DELETE /internal/users/{anon}/data", $"DELETE /internal/users/{anon}/data", $"POST trigger {OnlyUserId}"],
            calls);
        // the browser now has the account session
        Assert.Equal("lastfm", (await client.GetFromJsonAsync<JsonElement>("/api/v1/me", Ct)).GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Login_ImportStillRunning_TheLoginWorks_AndTheMoveIsRetriedUntilComplete()
    {
        var (client, sessionId) = await _factory.StartAnonymousAsync();
        var anon = $"anon_{sessionId}";
        TransferServices(anon, uploadsComplete: false);
        _factory.FakeLastFmLink();
        _factory.Fakes.On(HttpMethod.Post, "/internal/lastfm/link", WrapLinkToRecordTrigger([]));

        var resp = await _factory.LastFmCallbackAsync(client);

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal(AnonymousSessionStatus.Transferring, _factory.Sessions.Sessions[sessionId].Status);

        // still Transferring: the cleanup of expired sessions does not touch it
        _factory.Sessions.Sessions[sessionId].ExpiresAt = DateTime.UtcNow.AddDays(-1);
        await using var scope = _factory.Services.CreateAsyncScope();
        Assert.Equal(new SessionCleanupResult(0, 0), await scope.ServiceProvider.GetRequiredService<IAnonymousSessionService>().CleanupExpiredAsync(Ct));

        TransferServices(anon, uploadsComplete: true); // the import ended
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<IAccountService>().RetryTransfersAsync(Ct));
        Assert.Equal(AnonymousSessionStatus.Transferred, _factory.Sessions.Sessions[sessionId].Status);
    }

    [Fact]
    public async Task Login_WithAnExpiredAnonymousSession_MovesNothing()
    {
        var (client, sessionId) = await _factory.StartAnonymousAsync();
        _factory.Sessions.Sessions[sessionId].ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        var calls = TransferServices($"anon_{sessionId}");
        _factory.FakeLastFmLink();

        await _factory.LastFmCallbackAsync(client);

        Assert.Empty(calls);
        Assert.Single(_factory.Users.Users);
    }

    /// <summary>The fake link, plus the trigger of the pipeline for the user it creates.</summary>
    private Func<HttpRequestMessage, HttpResponseMessage> WrapLinkToRecordTrigger(List<string> calls) => req =>
    {
        var userId = JsonDocument.Parse(req.Content!.ReadAsStringAsync().Result).RootElement.GetProperty("userId").GetString()!;
        TriggerAnswers(calls, userId);
        _factory.Fakes.OnJson(HttpMethod.Get, $"/internal/lastfm/accounts/{userId}", new
        {
            userId, username = "alice", status = "Active", linkedAt = DateTime.UtcNow, lastScrobbleAt = DateTime.UtcNow, nextPollAt = DateTime.UtcNow
        });
        return FakeServices.Json(new { userId, username = "alice", importId = "i1" });
    };
}
