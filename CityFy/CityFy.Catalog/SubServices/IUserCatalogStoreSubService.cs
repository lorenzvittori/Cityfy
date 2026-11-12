using CityFy.Catalog.Models;

namespace CityFy.Catalog.SubServices;

/// <summary>The user's artist catalog ('user_catalogs' + 'user_artist_placements').</summary>
public interface IUserCatalogStoreSubService
{
    /// <summary>New random catalog version, used for the placements of a refresh.</summary>
    string NewVersion();

    /// <summary>Stores the placements as the user's current catalog and returns it.</summary>
    Task<UserCatalog> ReplaceAsync(string userId, string version, IReadOnlyList<ArtistPlacement> placements, DateTime now, CancellationToken ct);

    /// <summary>Current catalog; throws CITY_CATALOG_MISSING (404) when it was never built.</summary>
    Task<UserCatalog> GetRequiredAsync(string userId, CancellationToken ct);

    /// <summary>Placements of a given catalog version, keyed by the artist name of the plays.</summary>
    Task<IReadOnlyDictionary<string, ArtistPlacement>> GetPlacementsAsync(UserCatalog catalog, CancellationToken ct);
}
