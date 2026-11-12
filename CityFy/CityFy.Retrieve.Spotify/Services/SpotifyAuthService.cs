using CityFy.Retrieve.Spotify.Models;
using CityFy.Retrieve.Spotify.SubServices;

namespace CityFy.Retrieve.Spotify.Services;

public sealed class SpotifyAuthService(
    ISpotifyLoginStateSubService loginState,
    ISpotifyAuthorizeUrlSubService authorizeUrl,
    ISpotifyTokenExchangeSubService tokenExchange,
    ISpotifyAccountLinkSubService accountLink,
    ISpotifyAccessTokenSubService accessTokens,
    ILogger<SpotifyAuthService> logger) : ISpotifyAuthService
{
    public string CreateAuthorizationUrl() => authorizeUrl.Build(loginState.Create());

    public async Task<LoginResult> CompleteLoginAsync(string code, string? state, CancellationToken ct)
    {
        if (!loginState.TryConsume(state))
            return new LoginResult(LoginError.InvalidState);

        var token = await tokenExchange.ExchangeCodeAsync(code, ct);
        if (token == null)
            return new LoginResult(LoginError.TokenExchangeFailed);

        var account = await accountLink.LinkAsync(token, ct);
        accessTokens.Remember(account.UserId, token);

        logger.LogInformation("Spotify account {UserId} linked", account.UserId);
        return new LoginResult(LoginError.None, account);
    }
}
