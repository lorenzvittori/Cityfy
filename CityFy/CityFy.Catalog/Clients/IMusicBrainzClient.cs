using CityFy.Catalog.Models;

namespace CityFy.Catalog.Clients;

public interface IMusicBrainzClient
{
    /// <summary>Resolves any number of artists (split in batches of the API limit).</summary>
    Task<ResolveArtistsResponse> ResolveArtistsAsync(ResolveArtistsRequest request, CancellationToken ct);

    Task<GenreGraph> GetGenreGraphAsync(CancellationToken ct);
}
