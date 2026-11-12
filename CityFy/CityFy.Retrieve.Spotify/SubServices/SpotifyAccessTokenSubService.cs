using System.Security.Cryptography;
using CityFy.Retrieve.Spotify.Clients;
using CityFy.Retrieve.Spotify.Models;
using CityFy.Retrieve.Spotify.Repositories;
using CityFy.Retrieve.Spotify.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;

namespace CityFy.Retrieve.Spotify.SubServices;

public sealed class SpotifyAccessTokenSubService(
    ISpotifyAccountRepository repo,
    ISpotifyAuthClient authClient,
    IDataProtectionProvider dataProtection,
    IMemoryCache cache) : ISpotifyAccessTokenSubService
{
    /// <summary>Data Protection purpose of the stored refresh tokens (shared with the account link).</summary>
    public const string ProtectorPurpose = "CityFy.Retrieve.Spotify.RefreshToken";

    private const int DefaultLifetimeSeconds = 3600;
    private static readonly TimeSpan ExpirySafetyMargin = TimeSpan.FromSeconds(60);

    private readonly IDataProtector _protector = dataProtection.CreateProtector(ProtectorPurpose);

    public async Task<string> GetAsync(string userId, CancellationToken ct)
    {
        if (cache.TryGetValue<string>(Key(userId), out var cached) && cached != null)
            return cached;

        var account = await repo.GetAsync(userId, ct)
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

        var token = await authClient.RefreshAccessTokenAsync(refreshToken, ct);
        if (token?.AccessToken == null)
            throw SpotifyAccountException.ReauthRequired($"Spotify refused to refresh the token for '{userId}' (revoked?): log in again");

        // Spotify may rotate the refresh token
        if (!string.IsNullOrEmpty(token.RefreshToken) && token.RefreshToken != refreshToken)
            await repo.UpdateRefreshTokenAsync(userId, _protector.Protect(token.RefreshToken), ct);

        Remember(userId, token);
        return token.AccessToken;
    }

    public void Remember(string userId, TokenResponse token)
    {
        var lifetime = TimeSpan.FromSeconds(token.ExpiresIn ?? DefaultLifetimeSeconds) - ExpirySafetyMargin;
        if (lifetime > TimeSpan.Zero && token.AccessToken != null)
            cache.Set(Key(userId), token.AccessToken, lifetime);
    }

    public void Forget(string userId) => cache.Remove(Key(userId));

    private static string Key(string userId) => $"spotify:access-token:{userId}";
}