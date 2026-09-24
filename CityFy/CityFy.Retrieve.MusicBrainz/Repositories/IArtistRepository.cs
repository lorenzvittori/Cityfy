using CityFy.Retrieve.MusicBrainz.Models;

namespace CityFy.Retrieve.MusicBrainz.Repositories;

public interface IArtistRepository
{
    Task<Artist?> FindByMusicBrainzIdAsync(string? musicBrainzId, CancellationToken cancellationToken = default);
    Task<List<Artist>> FindByMusicBrainzIdsAsync(IEnumerable<string> musicBrainzIds, CancellationToken cancellationToken = default);
    Task<Artist?> FindByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<List<Artist>> FindByNamesAsync(IEnumerable<string> names, CancellationToken cancellationToken = default);
    Task<Artist> AddAsync(Artist artist, CancellationToken cancellationToken = default);
    Task<List<Artist>> AddRangeAsync(IEnumerable<Artist> artists, CancellationToken cancellationToken = default);
    Task<bool> RelationExistsAsync(int artistId, int genreId, string? relationType, CancellationToken cancellationToken = default);
    Task AddRelationAsync(ArtistGenreRelation relation, CancellationToken cancellationToken = default);
}
