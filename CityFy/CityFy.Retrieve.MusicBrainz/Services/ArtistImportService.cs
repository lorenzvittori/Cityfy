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

        if (artistsParsed?.Artists == null || !artistsParsed.Artists.Any())
        {
            _logger.LogInformation("No artists to import");
            return artistMapping;
        }

        const int pageSize = 200;
        var total = artistsParsed.Artists.Count;
        for (int offset = 0; offset < total; offset += pageSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = artistsParsed.Artists.Skip(offset).Take(pageSize).ToList();

            // raccolgo ids e nomi per query batch
            var mbids = chunk.Where(a => !string.IsNullOrEmpty(a.MusicBrainzId)).Select(a => a.MusicBrainzId!).Distinct().ToList();
            var names = chunk.Select(a => a.Name).Distinct().ToList();

            List<Artist> foundByMbid = new();
            List<Artist> foundByName = new();
            try
            {
                if (mbids.Any())
                {
                    foundByMbid = await _artistRepo.FindByMusicBrainzIdsAsync(mbids, cancellationToken);
                }
                if (names.Any())
                {
                    foundByName = await _artistRepo.FindByNamesAsync(names, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while searching batch of artists (offset={Offset})", offset);
            }

            var mbidLookup = foundByMbid.Where(a => !string.IsNullOrEmpty(a.MusicBrainzId)).ToDictionary(a => a.MusicBrainzId!, StringComparer.OrdinalIgnoreCase);
            var nameLookup = foundByName.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);

            // Determino quali aggiungere
            var toAdd = new List<Artist>();
            foreach (var src in chunk)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Artist? found = null;
                if (!string.IsNullOrEmpty(src.MusicBrainzId) && mbidLookup.TryGetValue(src.MusicBrainzId!, out var bymb))
                {
                    found = bymb;
                }
                else if (nameLookup.TryGetValue(src.Name, out var byname))
                {
                    found = byname;
                }

                if (found != null)
                {
                    artistMapping[src] = found;
                    continue;
                }

                // non trovato: aggiungo alla lista per inserimento batch
                toAdd.Add(new Artist { Name = src.Name, MusicBrainzId = src.MusicBrainzId });
            }

            if (toAdd.Any())
            {
                try
                {
                    var added = await _artistRepo.AddRangeAsync(toAdd, cancellationToken);
                    addedArtists += added.Count;

                    // mappo gli artisti aggiunti con le sorgenti
                    foreach (var src in chunk)
                    {
                        if (artistMapping.ContainsKey(src)) continue; // già mappato
                        var match = added.FirstOrDefault(a => string.Equals(a.Name, src.Name, StringComparison.OrdinalIgnoreCase) && string.Equals(a.MusicBrainzId, src.MusicBrainzId, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                        {
                            artistMapping[src] = match;
                        }
                    }

                    _logger.LogDebug("Added {Count} artists in batch (offset={Offset})", added.Count, offset);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to add batch of artists (offset={Offset})", offset);
                    // proviamo ad aggiungere singolarmente per non perdere gli altri
                    foreach (var src in chunk.Where(s => !artistMapping.ContainsKey(s)))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            var single = await _artistRepo.AddAsync(new Artist { Name = src.Name, MusicBrainzId = src.MusicBrainzId }, cancellationToken);
                            artistMapping[src] = single;
                            addedArtists++;
                        }
                        catch (Exception ex2)
                        {
                            _logger.LogError(ex2, "Failed to add single artist {Name} after batch failure", src.Name);
                        }
                    }
                }
            }
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
