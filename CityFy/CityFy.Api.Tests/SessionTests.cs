using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CityFy.Api.Models;
using CityFy.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using ServiceDefault.Errors;
using ServiceDefault.Models;

namespace CityFy.Api.Tests;

/// <summary>Anonymous sessions: start, validity on every request, renewal, expiry and cleanup of the data.</summary>
public sealed class SessionTests : IDisposable
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(1);

    private readonly ApiFactory _factory = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _factory.Dispose();

    private AnonymousSession Session(string id) => _factory.Sessions.Sessions[id];

    private static void AssertNear(DateTime expected, DateTime? actual) =>
        Assert.InRange(actual!.Value, expected - Tolerance, expected + Tolerance);

    private static async Task<string?> CodeOf(HttpResponseMessage resp) =>
        (await resp.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString();

    private static bool SetsAuthCookie(HttpResponseMessage resp) =>
        resp.Headers.TryGetValues("Set-Cookie", out var cookies) &&
        cookies.Any(c => c.StartsWith("cityfy.auth=", StringComparison.Ordinal) && !c.StartsWith("cityfy.auth=;", StringComparison.Ordinal));

    private static bool ClearsAuthCookie(HttpResponseMessage resp) =>
        resp.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith("cityfy.auth=;", StringComparison.Ordinal));

    /// <summary>Every service answers the deletion of <paramref name="userId"/> and records the call.</summary>
    private List<string> AllServicesDelete(string userId)
    {
        var calls = new List<string>();
        foreach (var path in new[] { $"/internal/lastfm/users/{userId}/data", $"/internal/users/{userId}/data" })
            _factory.Fakes.On(HttpMethod.Delete, path, _ =>
            {
                calls.Add(path);
                return FakeServices.Json(new UserDataDeletion("service", new Dictionary<string, long> { ["items"] = 1 }));
            });
        return calls;
    }

    private async Task<SessionCleanupResult> CleanupAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAnonymousSessionService>().CleanupExpiredAsync(Ct);
    }

    // --- start ---------------------------------------------------------------------------

    [Fact]
    public async Task Start_CreatesASessionOfOneDay_AndMeIsAnonymous()
    {
        var (client, sessionId) = await _factory.StartAnonymousAsync();

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me", Ct);

        Assert.Equal("anonymous", me.GetProperty("kind").GetString());
        Assert.Equal($"anon_{sessionId}", me.GetProperty("userId").GetString());
        AssertNear(DateTime.UtcNow.AddDays(1), me.GetProperty("expiresAt").GetDateTime().ToUniversalTime());
        Assert.Equal(32, sessionId.Length);
        Assert.Equal(AnonymousSessionStatus.Active, Session(sessionId).Status);
    }

    [Fact]
    public async Task Start_WithASessionAlready_ReturnsTheSameSession()
    {
        var (client, sessionId) = await _factory.StartAnonymousAsync();

        var resp = await client.PostAsync("/api/v1/session", null, Ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(sessionId, Assert.Single(_factory.Sessions.Sessions.Keys));
    }

    [Fact]
    public async Task AnonymousSession_IsTheOwnerSentToTheServices()
    {
        var (client, sessionId) = await _factory.StartAnonymousAsync();
        _factory.Fakes.OnJson(HttpMethod.Get, $"/api/pipeline/runs?userId=anon_{sessionId}&limit=10", Array.Empty<object>());

        var resp = await client.GetAsync("/api/v1/pipeline/runs", Ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task NoSession_IsUnauthenticated()
    {
        var resp = await _factory.CreateApiClient().GetAsync("/api/v1/me", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Equal(ErrorCodes.Unauthenticated, await CodeOf(resp));
    }

    // --- validity and renewal --------------------------------------------------------------

    [Fact]
    public async Task ExpiredSession_StillSentByTheBrowser_IsRefusedWithSessionExpired()
    {
        var (client, sessionId) = await _factory.StartAnonymousAsync();
        Session(sessionId).ExpiresAt = DateTime.UtcNow.AddMinutes(-1);

        var resp = await client.GetAsync("/api/v1/me", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Equal(ErrorCodes.SessionExpired, await CodeOf(resp));
        Assert.True(ClearsAuthCookie(resp));
    }

    [Fact]
    public async Task DeletedSession_IsRefusedWithSessionExpired()
    {
        var (client, sessionId) = await _factory.StartAnonymousAsync();
        Session(sessionId).Status = AnonymousSessionStatus.Deleted;

        var resp = await client.GetAsync("/api/v1/pipeline/runs", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Equal(ErrorCodes.SessionExpired, await CodeOf(resp));
    }

    [Fact]
    public async Task ExpiredSession_CanStartANewOne()
    {
        var (client, sessionId) = await _factory.StartAnonymousAsync();
        Session(sessionId).ExpiresAt = DateTime.UtcNow.AddMinutes(-1);

        var resp = await client.PostAsync("/api/v1/session", null, Ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(2, _factory.Sessions.Sessions.Count);
    }

    [Fact]
    public async Task VisitBeforeTheExpiry_AfterRenewAfter_MovesTheExpiryAndReissuesTheCookie()
    {
        var (client, sessionId) = await _factory.StartAnonymousAsync();
        var session = Session(sessionId);
        session.RenewedAt = DateTime.UtcNow.AddHours(-2);
        session.ExpiresAt = DateTime.UtcNow.AddHours(22);

        var resp = await client.GetAsync("/api/v1/me", Ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        AssertNear(DateTime.UtcNow.AddDays(1), session.ExpiresAt);
        AssertNear(DateTime.UtcNow, session.RenewedAt);
        Assert.True(SetsAuthCookie(resp));
    }

    [Fact]
    public async Task VisitWithinRenewAfter_DoesNotRenew()
    {
        var (client, sessionId) = await _factory.StartAnonymousAsync();
        var expiresAt = Session(sessionId).ExpiresAt;

        var resp = await client.GetAsync("/api/v1/me", Ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(expiresAt, Session(sessionId).ExpiresAt);
        Assert.False(SetsAuthCookie(resp));
    }

    // --- cleanup -------------------------------------------------------------------------

    [Fact]
    public async Task Cleanup_DeletesTheDataOfExpiredSessions_OnEveryService()
    {
        var (_, sessionId) = await _factory.StartAnonymousAsync();
        Session(sessionId).ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        var calls = AllServicesDelete($"anon_{sessionId}");

        var result = await CleanupAsync();

        Assert.Equal(new SessionCleanupResult(1, 0), result);
        Assert.Equal(5, calls.Count); // RetrieveLastFm + Dump, Orchestrator, CityGenerator, Catalog
        Assert.Equal(AnonymousSessionStatus.Deleted, Session(sessionId).Status);
        Assert.NotNull(Session(sessionId).DeletedAt);
    }

    [Fact]
    public async Task Cleanup_LeavesActiveSessionsAlone()
    {
        var (_, sessionId) = await _factory.StartAnonymousAsync();
        var calls = AllServicesDelete($"anon_{sessionId}");

        var result = await CleanupAsync();

        Assert.Equal(new SessionCleanupResult(0, 0), result);
        Assert.Empty(calls);
        Assert.Equal(AnonymousSessionStatus.Active, Session(sessionId).Status);
    }

    [Fact]
    public async Task Cleanup_ServiceDown_SessionStaysExpiredAndRefused_ThenTheNextRoundCompletes()
    {
        var (client, sessionId) = await _factory.StartAnonymousAsync();
        Session(sessionId).ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        var userId = $"anon_{sessionId}";
        AllServicesDelete(userId);
        _factory.Fakes.On(HttpMethod.Delete, $"/internal/lastfm/users/{userId}/data",
            _ => FakeServices.Problem(HttpStatusCode.ServiceUnavailable, ErrorCodes.ServiceUnavailable));

        Assert.Equal(new SessionCleanupResult(0, 1), await CleanupAsync());
        Assert.Equal(AnonymousSessionStatus.Expired, Session(sessionId).Status);
        Assert.Equal(ErrorCodes.SessionExpired, await CodeOf(await client.GetAsync("/api/v1/me", Ct)));

        AllServicesDelete(userId); // the service is back
        Assert.Equal(new SessionCleanupResult(1, 0), await CleanupAsync());
        Assert.Equal(AnonymousSessionStatus.Deleted, Session(sessionId).Status);
    }

    [Fact]
    public async Task DeleteMyData_EndsTheAnonymousSession()
    {
        var (client, sessionId) = await _factory.StartAnonymousAsync();
        var calls = AllServicesDelete($"anon_{sessionId}");

        var resp = await client.DeleteAsync("/api/v1/me/data", Ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(5, calls.Count);
        Assert.Equal(AnonymousSessionStatus.Deleted, Session(sessionId).Status);
        Assert.True(ClearsAuthCookie(resp));
    }
}
