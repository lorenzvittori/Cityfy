using CityFy.Retrieve.MusicBrainz.Data;
using CityFy.Retrieve.MusicBrainz.Models;
using Microsoft.EntityFrameworkCore;

namespace CityFy.Retrieve.MusicBrainz.Repositories;

public class ArtistRepository : IArtistRepository
{
    private readonly MusicBrainzContext _db;
    private readonly ILogger<ArtistRepository> _logger;

    public ArtistRepository(MusicBrainzContext db, ILogger<ArtistRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Artist?> FindByMusicBrainzIdAsync(string? musicBrainzId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(musicBrainzId)) return null;
        return await _db.Artists.FirstOrDefaultAsync(a => a.MusicBrainzId == musicBrainzId, cancellationToken);
    }

    public async Task<Artist?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _db.Artists.FirstOrDefaultAsync(a => a.Name == name, cancellationToken);
    }

    public async Task<Artist> AddAsync(Artist artist, CancellationToken cancellationToken = default)
    {
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync(cancellationToken);
        _logger?.LogDebug("Artist added: {Name} (Id={Id})", artist.Name, artist.Id);
        return artist;
    }

    public async Task<bool> RelationExistsAsync(int artistId, int genreId, string? relationType, CancellationToken cancellationToken = default)
    {
        return await _db.ArtistGenreRelations.AnyAsync(r => r.ArtistId == artistId && r.GenreId == genreId && r.RelationType == relationType, cancellationToken);
    }

    public async Task AddRelationAsync(ArtistGenreRelation relation, CancellationToken cancellationToken = default)
    {
        _db.ArtistGenreRelations.Add(relation);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
