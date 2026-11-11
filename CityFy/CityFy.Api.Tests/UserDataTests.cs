using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ServiceDefault.Errors;
using ServiceDefault.Models;

namespace CityFy.Api.Tests;

/// <summary>"Cancella i miei dati" (user) and the same operation from the admin panel.</summary>
public sealed class UserDataTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _factory.Dispose();

    /// <summary>The four services answer and record the order of the calls.</summary>
    private List<string> AllServicesDelete(string userId)
    {
        var calls = new List<string>();
        void On(string path, string service) => _factory.Fakes.On(HttpMethod.Delete, path, _ =>
        {
            calls.Add(service);
            return FakeServices.Json(new UserDataDeletion(service, new Dictionary<string, long> { ["items"] = 1 }));
        });
        On($"/internal/spotify/users/{userId}/data", "RetrieveSpotify");
        On($"/internal/users/{userId}/data", "shared"); // Dump, Orchestrator and Elaboration share the path
        return calls;
    }

    [Fact]
    public async Task User_DeletesTheirData_InEveryService_AndIsLoggedOut()
    {
        var calls = AllServicesDelete("u1");
        var client = await _factory.LoginAsync("u1");

        var resp = await client.DeleteAsync("/api/v1/me/data", Ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        // Spotify first (stops the retrieves), then dump, orchestrator, elaboration (plays last)
        Assert.Equal(["RetrieveSpotify", "shared", "shared", "shared"], calls);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("u1", body.GetProperty("userId").GetString());
        Assert.Equal(4, body.GetProperty("deleted").GetArrayLength());

        // session closed
        Assert.Contains(resp.Headers.GetValues("Set-Cookie"), c => c.StartsWith("cityfy.auth=;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ServiceDown_DeletionIsIncomplete_TheOthersStillRun_AndTheSessionStays()
    {
        var calls = AllServicesDelete("u1");
        _factory.Fakes.On(HttpMethod.Delete, "/internal/spotify/users/u1/data",
            _ => FakeServices.Problem(HttpStatusCode.ServiceUnavailable, ErrorCodes.ServiceUnavailable));
        var client = await _factory.LoginAsync("u1");

        var resp = await client.DeleteAsync("/api/v1/me/data", Ct);

        Assert.Equal(HttpStatusCode.BadGateway, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(ErrorCodes.UserDataDeletionIncomplete, problem.GetProperty("code").GetString());
        Assert.Equal(3, calls.Count); // dump, orchestrator, elaboration still deleted their part
        Assert.False(resp.Headers.TryGetValues("Set-Cookie", out _)); // can retry
    }

    [Fact]
    public async Task Anonymous_CannotDeleteData()
    {
        var resp = await _factory.CreateApiClient().DeleteAsync("/api/v1/me/data", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Admin_DeletesTheDataOfAUser()
    {
        _factory.AdminEnabled = true;
        var calls = AllServicesDelete("u2");
        var client = await _factory.AdminLoginAsync();

        var resp = await client.DeleteAsync("/api/admin/users/u2/data", Ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(4, calls.Count);
    }

    [Fact]
    public async Task NormalUser_CannotDeleteSomeoneElsesData()
    {
        _factory.AdminEnabled = true;
        AllServicesDelete("u2");
        var client = await _factory.LoginAsync("u1");

        var resp = await client.DeleteAsync("/api/admin/users/u2/data", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
