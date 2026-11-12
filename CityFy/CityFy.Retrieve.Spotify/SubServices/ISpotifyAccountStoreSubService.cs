using CityFy.Retrieve.Spotify.Models;

namespace CityFy.Retrieve.Spotify.SubServices;

/// <summary>Linked accounts ('spotify_accounts').</summary>
public interface ISpotifyAccountStoreSubService
{
    Task<SpotifyAccount?> GetAsync(string userId, CancellationToken ct);

    /// <summary>Throws SPOTIFY_ACCOUNT_NOT_LINKED (404) when the account is not linked.</summary>
    Task EnsureLinkedAsync(string userId, CancellationToken ct);

    /// <summary>Most recently linked first; refresh tokens are not loaded.</summary>
    Task<IReadOnlyList<SpotifyAccount>> ListAsync(CancellationToken ct);

    Task<IReadOnlyList<string>> GetAllUserIdsAsync(CancellationToken ct);

    /// <summary>Records a completed retrieve. The cursor only moves forward.</summary>
    Task UpdateRetrieveCursorAsync(string userId, DateTime retrievedAt, DateTime? lastPlayedAt, CancellationToken ct);

    /// <summary>Removes the account and its refresh token. Returns how many (0 or 1).</summary>
    Task<long> DeleteAsync(string userId, CancellationToken ct);
}