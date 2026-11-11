using CityFy.Retrieve.MusicBrainz.Models;

namespace CityFy.Retrieve.MusicBrainz.Services;

/// <summary>Download the latest export, extract the needed tables, import them.</summary>
public class RetrieveService
{
    private readonly IDumpDownloadService _downloader;
    private readonly ArchiveExtractor _extractor;
    private readonly MusicBrainzImporter _importer;
    private readonly ILogger<RetrieveService> _logger;

    public RetrieveService(IDumpDownloadService downloader, ArchiveExtractor extractor, MusicBrainzImporter importer, ILogger<RetrieveService> logger)
    {
        _downloader = downloader;
        _extractor = extractor;
        _importer = importer;
        _logger = logger;
    }

    public async Task<RetrieveResult> RunRetrieveAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting MusicBrainz retrieve job");

        var localDir = await _downloader.GetOrDownloadLatestAsync(DumpFiles.TablesByArchive.Keys.ToList(), cancellationToken);
        var extractDir = Path.Combine(localDir, "extracted");

        foreach (var (archive, tables) in DumpFiles.TablesByArchive)
        {
            // Extract only the tables still missing (re-runs and partially extracted dumps)
            var missing = tables.Where(t => !File.Exists(Path.Combine(extractDir, t))).ToList();
            if (missing.Count == 0) continue;

            _logger.LogInformation("Extracting {Count} tables from {Archive}", missing.Count, archive);
            await _extractor.ExtractAsync(Path.Combine(localDir, archive), extractDir, missing, cancellationToken);
        }

        var result = await _importer.ImportAsync(Path.Combine(extractDir, "mbdump"), cancellationToken);
        result.DumpName = Path.GetFileName(localDir);

        _logger.LogInformation(
            "MusicBrainz import {Dump} completed in {Duration}: {Genres} genres, {Relations} genre relations, {Artists} artists, {ArtistGenres} artist genres, {Links} Spotify links",
            result.DumpName, result.Duration, result.Genres, result.GenreRelations, result.Artists, result.ArtistGenres, result.SpotifyLinks);
        return result;
    }
}
