using CityFy.Retrieve.Spotify.Models;

namespace CityFy.Retrieve.Spotify.SubServices;

public interface ISpotifyAccountLinkSubService
{
    /// <summary>
    /// Stores (or updates) the account owning the token, with the refresh token encrypted.
    /// Throws SPOTIFY_TOKEN_EXCHANGE_FAILED (502) when Spotify did not return both tokens.
    /// </summary>
    Task<SpotifyAccount> LinkAsync(TokenResponse token, CancellationToken ct);
}