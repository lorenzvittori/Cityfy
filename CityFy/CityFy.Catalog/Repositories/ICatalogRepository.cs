using CityFy.Catalog.Models;

namespace CityFy.Catalog.Repositories;

public interface ICatalogRepository
{
    /// <summary>Per-artist play totals of a user in [since, until) (open ends when null).</summary>
    Task<List<ArtistPlays>> AggregateArtistPlaysAsync(string userId, DateTime? since, DateTime? until, int qualifiedPlayMs, CancellationToken ct);

    /// <summary>Has the user any play at all (any period, any source)?</summary>
    Task<bool> HasPlaysAsync(string userId, CancellationToken ct);

    Task<UserCatalog?> GetUserCatalogAsync(string userId, CancellationToken ct);

    /// <summary>Placements of one catalog version of the user.</summary>
    Task<List<ArtistPlacement>> GetPlacementsAsync(string userId, string catalogVersion, CancellationToken ct);

    /// <summary>
    /// Writes the placements of a new version, switches the user's catalog to it, then removes the
    /// placements of older versions (readers always see one complete version).
    /// </summary>
    Task ReplaceCatalogAsync(UserCatalog catalog, IReadOnlyList<ArtistPlacement> placements, CancellationToken ct);

    /// <summary>Data deletion: every play, placement and the catalog of the user (and the legacy period cities).</summary>
    Task<(long Plays, long Artists)> DeleteUserDataAsync(string userId, CancellationToken ct);
}
