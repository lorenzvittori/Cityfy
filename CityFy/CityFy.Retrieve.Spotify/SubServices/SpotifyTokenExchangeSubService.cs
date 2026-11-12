using CityFy.Retrieve.Spotify.Clients;
using CityFy.Retrieve.Spotify.Models;

namespace CityFy.Retrieve.Spotify.SubServices;

public sealed class SpotifyTokenExchangeSubService(ISpotifyAuthClient authClient) : ISpotifyTokenExchangeSubService
{
    public Task<TokenResponse?> ExchangeCodeAsync(string code, CancellationToken ct) => authClient.ExchangeCodeForTokenAsync(code, ct);
}