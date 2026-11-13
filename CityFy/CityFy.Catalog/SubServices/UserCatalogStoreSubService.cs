using CityFy.Catalog.Models;
using CityFy.Catalog.Repositories;
using ServiceDefault.Errors;

namespace CityFy.Catalog.SubServices;

public sealed class UserCatalogStoreSubService(ICatalogRepository repo) : IUserCatalogStoreSubService
{
    public string NewVersion() => Guid.NewGuid().ToString("N");

    public async Task<UserCatalog> ReplaceAsync(string userId, string version, IReadOnlyList<ArtistPlacement> placements, DateTime now, CancellationToken ct)
    {
        var catalog = new UserCatalog
        {
            UserId = userId,
            Version = version,
            UpdatedAt = now,
            Artists = placements.Count,
            PlacedArtists = placements.Count(p => p.IsPlaced)
        };
        await repo.ReplaceCatalogAsync(catalog, placements, ct);
        return catalog;
    }

    public async Task<UserCatalog> GetRequiredAsync(string userId, CancellationToken ct) =>
        await repo.GetUserCatalogAsync(userId, ct)
        ?? throw new CityFyException(ErrorCodes.CityCatalogMissing,
            $"No artist catalog for user '{userId}': the plays are still being imported", StatusCodes.Status404NotFound);

    public async Task<IReadOnlyDictionary<string, ArtistPlacement>> GetPlacementsAsync(UserCatalog catalog, CancellationToken ct) =>
        (await repo.GetPlacementsAsync(catalog.UserId, catalog.Version, ct)).ToDictionary(p => p.ArtistName);
}
