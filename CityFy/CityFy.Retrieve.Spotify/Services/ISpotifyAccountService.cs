using CityFy.Retrieve.Spotify.Models;

namespace CityFy.Retrieve.Spotify.Services;

public interface ISpotifyAccountService
{
    Task<SpotifyAccount?> GetAsync(string userId, CancellationToken ct);

    /// <summary>All linked accounts, most recently linked first (admin panel). Refresh tokens are not loaded.</summary>
    Task<IReadOnlyList<SpotifyAccount>> ListAsync(CancellationToken ct);

    /// <summary>Ids of every linked account (scheduled retrieve).</summary>
    Task<IReadOnlyList<string>> GetAllUserIdsAsync(CancellationToken ct);
}
