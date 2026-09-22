using CityFy.Retrieve.MusicBrainz.Models;

namespace CityFy.Retrieve.MusicBrainz.Repositories;

public interface IGenreRepository
{
    Task<Genre?> FindByMusicBrainzIdAsync(string? musicBrainzId, CancellationToken cancellationToken = default);
    Task<Genre?> FindByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<Genre> AddAsync(Genre genre, CancellationToken cancellationToken = default);
    Task<bool> RelationExistsAsync(int parentId, int childId, string? relationType, CancellationToken cancellationToken = default);
    Task AddRelationAsync(GenreRelation relation, CancellationToken cancellationToken = default);
}
