using CityFy.Retrieve.MusicBrainz.Models;
using CityFy.Retrieve.MusicBrainz.SubServices;

namespace CityFy.Retrieve.MusicBrainz.Services;

public sealed class MusicBrainzQueryService(IArtistResolveSubService artists, IGenreGraphSubService genres) : IMusicBrainzQueryService
{
    public Task<ResolveArtistsResponse> ResolveArtistsAsync(ResolveArtistsRequest request, CancellationToken ct) => artists.ResolveAsync(request, ct);

    public Task<GenreGraph> GetGenreGraphAsync(CancellationToken ct) => genres.GetGraphAsync(ct);

    public Task<IReadOnlyList<GenreNode>?> GetGenreRootsAsync(int genreId, CancellationToken ct) => genres.GetRootsAsync(genreId, ct);
}