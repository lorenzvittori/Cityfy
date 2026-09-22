using CityFy.Retrieve.MusicBrainz.Data;
using CityFy.Retrieve.MusicBrainz.Models;
using Microsoft.EntityFrameworkCore;

namespace CityFy.Retrieve.MusicBrainz.Repositories;

public class GenreRepository : IGenreRepository
{
    private readonly MusicBrainzContext _db;
    private readonly ILogger<GenreRepository> _logger;

    public GenreRepository(MusicBrainzContext db, ILogger<GenreRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Genre?> FindByMusicBrainzIdAsync(string? musicBrainzId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(musicBrainzId)) return null;
        return await _db.Genres.FirstOrDefaultAsync(g => g.MusicBrainzId == musicBrainzId, cancellationToken);
    }

    public async Task<Genre?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _db.Genres.FirstOrDefaultAsync(g => g.Name == name, cancellationToken);
    }

    public async Task<Genre> AddAsync(Genre genre, CancellationToken cancellationToken = default)
    {
        _db.Genres.Add(genre);
        await _db.SaveChangesAsync(cancellationToken);
        _logger?.LogDebug("Genre added: {Name} (Id={Id})", genre.Name, genre.Id);
        return genre;
    }

    public async Task<bool> RelationExistsAsync(int parentId, int childId, string? relationType, CancellationToken cancellationToken = default)
    {
        return await _db.GenreRelations.AnyAsync(r => r.ParentId == parentId && r.ChildId == childId && r.RelationType == relationType, cancellationToken);
    }

    public async Task AddRelationAsync(GenreRelation relation, CancellationToken cancellationToken = default)
    {
        _db.GenreRelations.Add(relation);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
