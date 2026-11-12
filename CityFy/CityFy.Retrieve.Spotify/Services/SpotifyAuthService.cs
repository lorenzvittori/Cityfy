using System.Security.Cryptography;
using CityFy.Retrieve.Spotify.Clients;
using CityFy.Retrieve.Spotify.Models;
using CityFy.Retrieve.Spotify.Options;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CityFy.Retrieve.Spotify.Services;

public sealed class SpotifyAuthService(
    IOptions<SpotifyOptions> options,
    ISpotifyAuthClient authClient,
    ISpotifyAccountService accounts,
    IMemoryCache cache) : ISpotifyAuthService
{
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    private readonly SpotifyOptions _options = options.Value;

    public string CreateAuthorizationUrl()
    {
        // Unguessable one-time value bound to this login attempt (CSRF protection).
        // In-memory: with more than one instance it must move to a shared store.
        var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        cache.Set(StateKey(state), true, StateLifetime);

        return QueryHelpers.AddQueryString($"{_options.AccountsBase.TrimEnd('/')}/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = _options.ClientId,
            ["scope"] = _options.Scopes,
            ["redirect_uri"] = _options.RedirectUri,
            ["state"] = state,
            ["show_dialog"] = "true"
        });
    }

    public async Task<LoginResult> CompleteLoginAsync(string code, string? state, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(state) || !cache.TryGetValue(StateKey(state), out _))
            return new LoginResult(LoginError.InvalidState);

        // One-time use
        cache.Remove(StateKey(state));

        var token = await authClient.ExchangeCodeForTokenAsync(code, ct);
        if (token == null)
            return new LoginResult(LoginError.TokenExchangeFailed);

        var account = await accounts.LinkAsync(token, ct);
        return new LoginResult(LoginError.None, account);
    }

    private static string StateKey(string state) => $"spotify:oauth-state:{state}";
}
