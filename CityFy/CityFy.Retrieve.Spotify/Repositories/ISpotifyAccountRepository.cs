using CityFy.Retrieve.Spotify.Models;

namespace CityFy.Retrieve.Spotify.Repositories;

public interface ISpotifyAccountRepository
{
    /// <summary>Insert or update (re-login). LinkedAt and the retrieve cursor are preserved.</summary>
    Task UpsertAsync(SpotifyAccount account, CancellationToken ct);
    Task<SpotifyAccount?> GetAsync(string userId, CancellationToken ct);
    Task UpdateRefreshTokenAsync(string userId, string protectedRefreshToken, CancellationToken ct);

    /// <summary>Records a completed retrieve. The cursor only moves forward.</summary>
    Task UpdateRetrieveCursorAsync(string userId, DateTime retrievedAt, DateTime? lastPlayedAt, CancellationToken ct);

    Task<IReadOnlyList<string>> GetAllUserIdsAsync(CancellationToken ct);

    /// <summary>All accounts, most recently linked first. ProtectedRefreshToken is NOT loaded.</summary>
    Task<IReadOnlyList<SpotifyAccount>> ListAsync(CancellationToken ct);

    /// <summary>Removes the account and its refresh token (data deletion). Returns how many (0 or 1).</summary>
    Task<long> DeleteAsync(string userId, CancellationToken ct);
}
