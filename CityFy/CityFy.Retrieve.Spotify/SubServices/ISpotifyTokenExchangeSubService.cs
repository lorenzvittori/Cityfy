using CityFy.Retrieve.Spotify.Models;

namespace CityFy.Retrieve.Spotify.SubServices;

public interface ISpotifyTokenExchangeSubService
{
    /// <summary>Authorization code -> tokens. Null when Spotify refuses the code.</summary>
    Task<TokenResponse?> ExchangeCodeAsync(string code, CancellationToken ct);
}