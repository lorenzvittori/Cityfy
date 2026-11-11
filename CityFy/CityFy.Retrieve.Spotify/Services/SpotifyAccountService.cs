using System.Security.Cryptography;
using CityFy.Retrieve.Spotify.Clients;
using CityFy.Retrieve.Spotify.Models;
using CityFy.Retrieve.Spotify.Repositories;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using ServiceDefault.Errors;

namespace CityFy.Retrieve.Spotify.Services
{
    public interface ISpotifyAccountService
    {
        /// <summary>Stores (or updates) the account owning the token. Returns it.</summary>
        Task<SpotifyAccount> LinkAsync(TokenResponse token, CancellationToken ct);

        Task<SpotifyAccount?> GetAsync(string userId, CancellationToken ct);

        /// <summary>
        /// Valid access token for the user: from memory if still valid, otherwise refreshed with
        /// the stored refresh token. Throws <see cref="SpotifyAccountException"/> when the account
        /// must be linked again.
        /// </summary>
        Task<string> GetAccessTokenAsync(string userId, CancellationToken ct);
    }

    /// <summary>The linked account cannot be used: not linked (404) or must log in again (401).</summary>
    public class SpotifyAccountException(string code, string message, int statusCode)
        : CityFyException(code, message, statusCode)
    {
        public static SpotifyAccountException NotLinked(string userId) =>
            new(ErrorCodes.SpotifyAccountNotLinked, $"Spotify account '{userId}' is not linked: log in via /debug/spotify/login", StatusCodes.Status404NotFound);

        public static SpotifyAccountException ReauthRequired(string message) =>
            new(ErrorCodes.SpotifyReauthRequired, message, StatusCodes.Status401Unauthorized);
    }

    public class SpotifyAccountService : ISpotifyAccountService
    {
        private const string ProtectorPurpose = "CityFy.Retrieve.Spotify.RefreshToken";
        private static readonly TimeSpan ExpirySafetyMargin = TimeSpan.FromSeconds(60);

        private readonly ISpotifyAccountRepository _repo;
        private readonly ISpotifyAuthClient _authClient;
        private readonly ISpotifyClient _client;
        private readonly IDataProtector _protector;
        private readonly IMemoryCache _cache;
        private readonly ILogger<SpotifyAccountService> _logger;

        public SpotifyAccountService(
            ISpotifyAccountRepository repo,
            ISpotifyAuthClient authClient,
            ISpotifyClient client,
            IDataProtectionProvider dataProtection,
            IMemoryCache cache,
            ILogger<SpotifyAccountService> logger)
        {
            _repo = repo;
            _authClient = authClient;
            _client = client;
            _protector = dataProtection.CreateProtector(ProtectorPurpose);
            _cache = cache;
            _logger = logger;
        }

        public async Task<SpotifyAccount> LinkAsync(TokenResponse token, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(token.AccessToken) || string.IsNullOrEmpty(token.RefreshToken))
                throw new CityFyException(ErrorCodes.SpotifyTokenExchangeFailed, "Spotify did not return both an access and a refresh token", StatusCodes.Status502BadGateway);

            var user = await _client.GetCurrentUserAsync(token.AccessToken, ct);

            var account = new SpotifyAccount
            {
                UserId = user.Id!,
                DisplayName = user.DisplayName,
                ProtectedRefreshToken = _protector.Protect(token.RefreshToken),
                Scope = token.Scope
            };
            await _repo.UpsertAsync(account, ct);
            CacheAccessToken(account.UserId, token);

            _logger.LogInformation("Spotify account {UserId} linked", account.UserId);
            return account;
        }

        public Task<SpotifyAccount?> GetAsync(string userId, CancellationToken ct) => _repo.GetAsync(userId, ct);

        public async Task<string> GetAccessTokenAsync(string userId, CancellationToken ct)
        {
            if (_cache.TryGetValue<string>(CacheKey(userId), out var cached) && cached != null)
                return cached;

            var account = await _repo.GetAsync(userId, ct)
                ?? throw SpotifyAccountException.NotLinked(userId);

            string refreshToken;
            try
            {
                refreshToken = _protector.Unprotect(account.ProtectedRefreshToken);
            }
            catch (CryptographicException)
            {
                // Data Protection keys lost/rotated (e.g. container without a persisted key ring)
                throw SpotifyAccountException.ReauthRequired($"Stored refresh token for '{userId}' cannot be decrypted: log in again");
            }

            var token = await _authClient.RefreshAccessTokenAsync(refreshToken, ct);
            if (token?.AccessToken == null)
                throw SpotifyAccountException.ReauthRequired($"Spotify refused to refresh the token for '{userId}' (revoked?): log in again");

            // Spotify may rotate the refresh token
            if (!string.IsNullOrEmpty(token.RefreshToken) && token.RefreshToken != refreshToken)
                await _repo.UpdateRefreshTokenAsync(userId, _protector.Protect(token.RefreshToken), ct);

            CacheAccessToken(userId, token);
            return token.AccessToken;
        }

        private void CacheAccessToken(string userId, TokenResponse token)
        {
            var lifetime = TimeSpan.FromSeconds(token.ExpiresIn ?? 3600) - ExpirySafetyMargin;
            if (lifetime > TimeSpan.Zero)
                _cache.Set(CacheKey(userId), token.AccessToken, lifetime);
        }

        private static string CacheKey(string userId) => $"spotify:access-token:{userId}";
    }
}
