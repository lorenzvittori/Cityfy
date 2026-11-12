using CityFy.Retrieve.Spotify.Models;
using CityFy.Retrieve.Spotify.Repositories;
using CityFy.Retrieve.Spotify.Services;

namespace CityFy.Retrieve.Spotify.SubServices;

public sealed class SpotifyAccountStoreSubService(ISpotifyAccountRepository repo) : ISpotifyAccountStoreSubService
{
    public Task<SpotifyAccount?> GetAsync(string userId, CancellationToken ct) => repo.GetAsync(userId, ct);

    public async Task EnsureLinkedAsync(string userId, CancellationToken ct)
    {
        if (await repo.GetAsync(userId, ct) == null)
            throw SpotifyAccountException.NotLinked(userId);
    }

    public Task<IReadOnlyList<SpotifyAccount>> ListAsync(CancellationToken ct) => repo.ListAsync(ct);

    public Task<IReadOnlyList<string>> GetAllUserIdsAsync(CancellationToken ct) => repo.GetAllUserIdsAsync(ct);

    public Task UpdateRetrieveCursorAsync(string userId, DateTime retrievedAt, DateTime? lastPlayedAt, CancellationToken ct) =>
        repo.UpdateRetrieveCursorAsync(userId, retrievedAt, lastPlayedAt, ct);

    public Task<long> DeleteAsync(string userId, CancellationToken ct) => repo.DeleteAsync(userId, ct);
}