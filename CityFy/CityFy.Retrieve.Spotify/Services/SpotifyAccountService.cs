using CityFy.Retrieve.Spotify.Models;
using CityFy.Retrieve.Spotify.SubServices;

namespace CityFy.Retrieve.Spotify.Services;

public sealed class SpotifyAccountService(ISpotifyAccountStoreSubService accounts) : ISpotifyAccountService
{
    public Task<SpotifyAccount?> GetAsync(string userId, CancellationToken ct) => accounts.GetAsync(userId, ct);

    public Task<IReadOnlyList<SpotifyAccount>> ListAsync(CancellationToken ct) => accounts.ListAsync(ct);

    public Task<IReadOnlyList<string>> GetAllUserIdsAsync(CancellationToken ct) => accounts.GetAllUserIdsAsync(ct);
}
