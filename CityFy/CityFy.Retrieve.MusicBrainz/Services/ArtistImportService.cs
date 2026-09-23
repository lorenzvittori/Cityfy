using CityFy.Retrieve.MusicBrainz.Models;
using CityFy.Retrieve.MusicBrainz.Repositories;
using Microsoft.Extensions.Logging;

namespace CityFy.Retrieve.MusicBrainz.Services;

public interface IArtistImportService
{
    Task<Dictionary<Artist, Artist>> ImportArtistsAsync(ArtistParser.ParsedResult artistsParsed, CancellationToken cancellationToken = default);
    Task<int> ImportArtistRelationsAsync(ArtistParser.ParsedResult artistsParsed, GenreParser.ParsedResult genresParsed, Dictionary<Genre, Genre> genreMapping, Dictionary<Artist, Artist> artistMapping, CancellationToken cancellationToken = default);
}

public class ArtistImportService : IArtistImportService
{
    private readonly IArtistRepository _artistRepo;
    private readonly ILogger<ArtistImportService> _logger;

    public ArtistImportService(IArtistRepository artistRepo, ILogger<ArtistImportService> logger)
    {
        _artistRepo = artistRepo;
        _logger = logger;
    }

    public async Task<Dictionary<Artist, Artist>> ImportArtistsAsync(ArtistParser.ParsedResult artistsParsed, CancellationToken cancellationToken = default)
    {
        var artistMapping = new Dictionary<Artist, Artist>();
        int addedArtists = 0;
        int candidateCount = artistsParsed?.Artists?.Count ?? 0;

        foreach (var src in artistsParsed.Artists)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Artist? found = null;
            try
            {
                if (!string.IsNullOrEmpty(src.MusicBrainzId))
                {
                    found = await _artistRepo.FindByMusicBrainzIdAsync(src.MusicBrainzId, cancellationToken);
                }
                if (found == null)
                {
                    found = await _artistRepo.FindByNameAsync(src.Name, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while searching for artist '{Name}'", src.Name);
            }

            if (found == null)
            {
                var toAdd = new Artist { Name = src.Name, MusicBrainzId = src.MusicBrainzId };
                try
                {
                    found = await _artistRepo.AddAsync(toAdd, cancellationToken);
                    addedArtists++;
                    _logger.LogDebug("Added artist {Name} with Id {Id}", found.Name, found.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to add artist {Name}", src.Name);
                    continue;
                }
            }

            artistMapping[src] = found;
        }

        _logger.LogInformation("Artists processed: {Processed}, added: {Added}", artistsParsed.Artists.Count, addedArtists);
        _logger.LogInformation("Artist import summary: candidates={Candidates}, added={Added}", candidateCount, addedArtists);
        return artistMapping;
    }

    public async Task<int> ImportArtistRelationsAsync(ArtistParser.ParsedResult artistsParsed, GenreParser.ParsedResult genresParsed, Dictionary<Genre, Genre> genreMapping, Dictionary<Artist, Artist> artistMapping, CancellationToken cancellationToken = default)
    {
        int addedArtistRelations = 0;
        foreach (var rel in artistsParsed.Relations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!artistsParsed.Artists.Any()) continue;
            var srcArtist = artistsParsed.Artists.FirstOrDefault(a => a.Id == rel.ArtistId);
            if (srcArtist == null)
            {
                _logger.LogDebug("Skipping artist relation because source artist not found for dump id {Id}", rel.ArtistId);
                continue;
            }

            if (!artistMapping.TryGetValue(srcArtist, out var dbArtist))
            {
                _logger.LogDebug("Skipping relation because artist mapping missing for {Name}", srcArtist.Name);
                continue;
            }

            if (!genresParsed.GenresByDumpId.TryGetValue(rel.GenreId, out var srcGenre))
            {
                _logger.LogDebug("Skipping artist relation because genre dump id {Id} not found", rel.GenreId);
                continue;
            }

            if (!genreMapping.TryGetValue(srcGenre, out var dbGenre))
            {
                _logger.LogDebug("Skipping artist relation because db genre mapping missing for {Name}", srcGenre.Name);
                continue;
            }

            try
            {
                var exists = await _artistRepo.RelationExistsAsync(dbArtist.Id, dbGenre.Id, rel.RelationType, cancellationToken);
                if (!exists)
                {
                    var newRel = new ArtistGenreRelation { ArtistId = dbArtist.Id, GenreId = dbGenre.Id, RelationType = rel.RelationType };
                    await _artistRepo.AddRelationAsync(newRel, cancellationToken);
                    addedArtistRelations++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add artist-genre relation artistId={ArtistId} genreId={GenreId}", dbArtist.Id, dbGenre.Id);
            }
        }

        _logger.LogInformation("Artist relations processed: {Processed}, added: {Added}", artistsParsed.Relations.Count, addedArtistRelations);
        return addedArtistRelations;
    }
}
