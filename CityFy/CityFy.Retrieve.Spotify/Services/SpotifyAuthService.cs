using System.Security.Cryptography;
using CityFy.Retrieve.Spotify.Clients;
using CityFy.Retrieve.Spotify.Options;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CityFy.Retrieve.Spotify.Services
{
    public class SpotifyAuthService : ISpotifyAuthService
    {
        private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

        private readonly SpotifyOptions _options;
        private readonly ISpotifyAuthClient _authClient;
        private readonly ISpotifyAccountService _accounts;
        private readonly IMemoryCache _cache;

        public SpotifyAuthService(
            IOptions<SpotifyOptions> options,
            ISpotifyAuthClient authClient,
            ISpotifyAccountService accounts,
            IMemoryCache cache)
        {
            _options = options.Value;
            _authClient = authClient;
            _accounts = accounts;
            _cache = cache;
        }

        public string CreateAuthorizationUrl()
        {
            // Unguessable one-time value bound to this login attempt (CSRF protection).
            // In-memory: with more than one instance it must move to a shared store.
            var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            _cache.Set(StateKey(state), true, StateLifetime);

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
            if (string.IsNullOrEmpty(state) || !_cache.TryGetValue(StateKey(state), out _))
                return new LoginResult(LoginError.InvalidState);

            // One-time use
            _cache.Remove(StateKey(state));

            var token = await _authClient.ExchangeCodeForTokenAsync(code, ct);
            if (token == null)
                return new LoginResult(LoginError.TokenExchangeFailed);

            var account = await _accounts.LinkAsync(token, ct);
            return new LoginResult(LoginError.None, account);
        }

        private static string StateKey(string state) => $"spotify:oauth-state:{state}";
    }
}
