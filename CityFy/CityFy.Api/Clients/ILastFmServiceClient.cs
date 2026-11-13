using System.Text.Json;
using ServiceDefault.Models;

namespace CityFy.Api.Clients;

/// <summary>Retrieve.LastFm: login with Last.fm (authorize, link), linked accounts, history imports.</summary>
public interface ILastFmServiceClient : IUserDataOwner
{
    /// <summary>Last.fm page where the user authorizes CityFy (it comes back to the gateway callback with ?token=).</summary>
    Task<string> GetAuthorizeUrlAsync(CancellationToken ct);

    /// <summary>
    /// Token → linked account. <paramref name="newUserId"/> is used for a Last.fm account seen for the
    /// first time; otherwise the answer carries the id of its existing owner. Throws LASTFM_AUTH_FAILED
    /// (400), LASTFM_UNAVAILABLE (502).
    /// </summary>
    Task<LastFmLinkResult> LinkAsync(string newUserId, string token, CancellationToken ct);

    /// <summary>Throws LASTFM_NOT_LINKED (404).</summary>
    Task<LastFmAccountInfo> GetAccountAsync(string userId, CancellationToken ct);

    /// <summary>Throws LASTFM_IMPORT_NOT_FOUND (404).</summary>
    Task<LastFmImportInfo> GetImportAsync(string importId, CancellationToken ct);

    /// <summary>Admin: every linked account with polling state and track playing (no session keys).</summary>
    Task<JsonElement> ListAccountsAsync(CancellationToken ct);

    /// <summary>Admin: most recent history imports, with progress.</summary>
    Task<JsonElement> ListImportsAsync(int limit, CancellationToken ct);

    /// <summary>Admin: imports the whole history again. Throws LASTFM_NOT_LINKED (404).</summary>
    Task<JsonElement> StartImportAsync(string userId, CancellationToken ct);

    /// <summary>Admin: reads the new scrobbles at the next polling tick. Throws LASTFM_NOT_LINKED (404).</summary>
    Task PollNowAsync(string userId, CancellationToken ct);

    /// <summary>End of the Last.fm licence: every account and import of Retrieve.LastFm (not the plays).</summary>
    Task<UserDataDeletion> DeleteEverythingAsync(CancellationToken ct);

    /// <summary>Admin. Throws LASTFM_IMPORT_NOT_FOUND (404), PROCESS_NOT_CANCELLABLE (409).</summary>
    Task<JsonElement> CancelImportAsync(string importId, CancellationToken ct);
}
