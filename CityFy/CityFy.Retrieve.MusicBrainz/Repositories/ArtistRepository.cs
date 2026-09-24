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

    public async Task<List<Artist>> FindByMusicBrainzIdsAsync(IEnumerable<string> musicBrainzIds, CancellationToken cancellationToken = default)
    {
        var ids = musicBrainzIds?.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList() ?? new List<string>();
        if (!ids.Any()) return new List<Artist>();
        return await _db.Artists.Where(a => ids.Contains(a.MusicBrainzId)).ToListAsync(cancellationToken);
    }

    public async Task<Artist?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _db.Artists.FirstOrDefaultAsync(a => a.Name == name, cancellationToken);
    }

    public async Task<List<Artist>> FindByNamesAsync(IEnumerable<string> names, CancellationToken cancellationToken = default)
    {
        var list = names?.Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList() ?? new List<string>();
        if (!list.Any()) return new List<Artist>();
        return await _db.Artists.Where(a => list.Contains(a.Name)).ToListAsync(cancellationToken);
    }

    public async Task<Artist> AddAsync(Artist artist, CancellationToken cancellationToken = default)
    {
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync(cancellationToken);
        _logger?.LogDebug("Artist added: {Name} (Id={Id})", artist.Name, artist.Id);
        return artist;
    }

    public async Task<List<Artist>> AddRangeAsync(IEnumerable<Artist> artists, CancellationToken cancellationToken = default)
    {
        var list = artists?.ToList() ?? new List<Artist>();
        if (!list.Any()) return new List<Artist>();
        _db.Artists.AddRange(list);
        await _db.SaveChangesAsync(cancellationToken);
        _logger?.LogDebug("Added {Count} artists", list.Count);
        return list;
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
