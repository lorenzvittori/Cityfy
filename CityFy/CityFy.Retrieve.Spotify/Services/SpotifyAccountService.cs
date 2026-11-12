using System.Security.Cryptography;
using CityFy.Retrieve.Spotify.Clients;
using CityFy.Retrieve.Spotify.Models;
using CityFy.Retrieve.Spotify.Repositories;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using ServiceDefault.Errors;

namespace CityFy.Retrieve.Spotify.Services;

public sealed class SpotifyAccountService(
    ISpotifyAccountRepository repo,
    ISpotifyAuthClient authClient,
    ISpotifyClient client,
    IDataProtectionProvider dataProtection,
    IMemoryCache cache,
    ILogger<SpotifyAccountService> logger) : ISpotifyAccountService
{
    private const string ProtectorPurpose = "CityFy.Retrieve.Spotify.RefreshToken";
    private static readonly TimeSpan ExpirySafetyMargin = TimeSpan.FromSeconds(60);

    private readonly IDataProtector _protector = dataProtection.CreateProtector(ProtectorPurpose);

    public async Task<SpotifyAccount> LinkAsync(TokenResponse token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token.AccessToken) || string.IsNullOrEmpty(token.RefreshToken))
            throw new CityFyException(ErrorCodes.SpotifyTokenExchangeFailed, "Spotify did not return both an access and a refresh token", StatusCodes.Status502BadGateway);

        var user = await client.GetCurrentUserAsync(token.AccessToken, ct);

        var account = new SpotifyAccount
        {
            UserId = user.Id!,
            DisplayName = user.DisplayName,
            ProtectedRefreshToken = _protector.Protect(token.RefreshToken),
            Scope = token.Scope
        };
        await repo.UpsertAsync(account, ct);
        CacheAccessToken(account.UserId, token);

        logger.LogInformation("Spotify account {UserId} linked", account.UserId);
        return account;
    }

    public Task<SpotifyAccount?> GetAsync(string userId, CancellationToken ct) => repo.GetAsync(userId, ct);

    public Task<IReadOnlyList<SpotifyAccount>> ListAsync(CancellationToken ct) => repo.ListAsync(ct);

    public Task<IReadOnlyList<string>> GetAllUserIdsAsync(CancellationToken ct) => repo.GetAllUserIdsAsync(ct);

    public async Task<string> GetAccessTokenAsync(string userId, CancellationToken ct)
    {
        if (cache.TryGetValue<string>(CacheKey(userId), out var cached) && cached != null)
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

        CacheAccessToken(userId, token);
        return token.AccessToken;
    }

    private void CacheAccessToken(string userId, TokenResponse token)
    {
        var lifetime = TimeSpan.FromSeconds(token.ExpiresIn ?? 3600) - ExpirySafetyMargin;
        if (lifetime > TimeSpan.Zero)
            cache.Set(CacheKey(userId), token.AccessToken, lifetime);
    }

    public void Forget(string userId) => cache.Remove(CacheKey(userId));

    private static string CacheKey(string userId) => $"spotify:access-token:{userId}";
}
