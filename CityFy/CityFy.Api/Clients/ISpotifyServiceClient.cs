using System.Text.Json;

namespace CityFy.Api.Clients;

/// <summary>Retrieve.Spotify: login, linked account, retrieve jobs.</summary>
public interface ISpotifyServiceClient : IUserDataOwner
{
    Task<string> GetAuthorizeUrlAsync(CancellationToken ct);

    /// <summary>Throws SPOTIFY_LOGIN_EXPIRED (400) or SPOTIFY_TOKEN_EXCHANGE_FAILED (502).</summary>
    Task<SpotifyLinkResult> LinkAsync(string code, string? state, CancellationToken ct);

    /// <summary>Throws SPOTIFY_ACCOUNT_NOT_LINKED (404).</summary>
    Task<SpotifyAccountInfo> GetAccountAsync(string userId, CancellationToken ct);

    Task<JobAccepted> StartRetrieveAsync(string userId, CancellationToken ct);

    Task<SpotifyJobInfo> GetJobAsync(string jobId, CancellationToken ct);

    /// <summary>Admin: most recent retrieve jobs of every user, with progress (passed through as is).</summary>
    Task<JsonElement> ListJobsAsync(int limit, CancellationToken ct);

    Task<JsonElement> CancelJobAsync(string jobId, CancellationToken ct);

    /// <summary>Admin: every linked account (no tokens).</summary>
    Task<List<SpotifyAccountInfo>> ListAccountsAsync(CancellationToken ct);
}
