using CityFy.Retrieve.MusicBrainz.Models;
using CityFy.Retrieve.MusicBrainz.Repositories;
using Microsoft.Extensions.Logging;

namespace CityFy.Retrieve.MusicBrainz.Services;

public class DataImporter
{
    private readonly IGenreImportService _genreImport;
    private readonly IArtistImportService? _artistImport;
    private readonly ILogger<DataImporter> _logger;

    public DataImporter(IGenreImportService genreImport, ILogger<DataImporter> logger, IArtistImportService? artistImport = null)
    {
        _genreImport = genreImport;
        _logger = logger;
        _artistImport = artistImport;
    }

    // Importa generi e relazioni usando il repository (no SQL manuale)
    public async Task ImportAsync(GenreParser.ParsedResult parsed, ArtistParser.ParsedResult? artistsParsed = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting import: {GenreCount} genres, {RelationCount} relations", parsed.Genres.Count, parsed.Relations.Count);

        // import genres and relations via dedicated subservice
        var mapping = await _genreImport.ImportGenresAsync(parsed, cancellationToken);
        var addedRelations = await _genreImport.ImportRelationsAsync(parsed, mapping, cancellationToken);

        // If artist data available and artist import subservice provided, delegate artist import
        if (artistsParsed != null && _artistImport != null)
        {
            _logger.LogInformation("Starting artist import: {ArtistCount} artists, {RelCount} relations", artistsParsed.Artists.Count, artistsParsed.Relations.Count);
            var artistMapping = await _artistImport.ImportArtistsAsync(artistsParsed, cancellationToken);
            var addedArtistRelations = await _artistImport.ImportArtistRelationsAsync(artistsParsed, parsed, mapping, artistMapping, cancellationToken);
            _logger.LogInformation("Artist import complete: added {AddedArtists} artists, added {AddedRelations} artist relations", artistMapping.Count, addedArtistRelations);
        }
    }
}
