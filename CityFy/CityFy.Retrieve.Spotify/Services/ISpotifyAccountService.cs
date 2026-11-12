using CityFy.Retrieve.Spotify.Models;

namespace CityFy.Retrieve.Spotify.Services;

public interface ISpotifyAccountService
{
    /// <summary>Stores (or updates) the account owning the token. Returns it.</summary>
    Task<SpotifyAccount> LinkAsync(TokenResponse token, CancellationToken ct);

    Task<SpotifyAccount?> GetAsync(string userId, CancellationToken ct);

    /// <summary>All linked accounts, most recently linked first (admin panel). Refresh tokens are not loaded.</summary>
    Task<IReadOnlyList<SpotifyAccount>> ListAsync(CancellationToken ct);

    /// <summary>Ids of every linked account (scheduled retrieve).</summary>
    Task<IReadOnlyList<string>> GetAllUserIdsAsync(CancellationToken ct);

    /// <summary>
    /// Valid access token for the user: from memory if still valid, otherwise refreshed with
    /// the stored refresh token. Throws <see cref="SpotifyAccountException"/> when the account
    /// must be linked again.
    /// </summary>
    Task<string> GetAccessTokenAsync(string userId, CancellationToken ct);

    /// <summary>Forgets the cached access token (account deleted).</summary>
    void Forget(string userId);
}
