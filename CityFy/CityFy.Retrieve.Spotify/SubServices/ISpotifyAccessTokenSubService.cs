using CityFy.Retrieve.Spotify.Models;

namespace CityFy.Retrieve.Spotify.SubServices;

/// <summary>Access tokens: kept in memory only, refreshed with the stored (encrypted) refresh token.</summary>
public interface ISpotifyAccessTokenSubService
{
    /// <summary>
    /// Valid access token: from memory if still valid, otherwise refreshed (and the refresh token
    /// rotated if Spotify sends a new one). Throws SpotifyAccountException when the account must
    /// be linked again.
    /// </summary>
    Task<string> GetAsync(string userId, CancellationToken ct);

    /// <summary>Keeps a fresh access token in memory until shortly before it expires.</summary>
    void Remember(string userId, TokenResponse token);

    /// <summary>Forgets the cached access token (account deleted).</summary>
    void Forget(string userId);
}