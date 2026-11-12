using CityFy.Retrieve.Spotify.Models;

namespace CityFy.Retrieve.Spotify.Clients;

public interface ISpotifyAuthClient
{
    // authorization_code -> access + refresh token. Null on failure (logged).
    Task<TokenResponse?> ExchangeCodeForTokenAsync(string code, CancellationToken ct = default);

    // refresh_token -> new access token (and possibly a new refresh token). Null on failure (logged).
    Task<TokenResponse?> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct = default);
}
