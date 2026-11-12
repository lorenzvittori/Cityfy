using CityFy.Retrieve.MusicBrainz.Clients;
using CityFy.Retrieve.MusicBrainz.Models;

namespace CityFy.Retrieve.MusicBrainz.Services;

/// <summary>Download the latest export, extract the needed tables, import them.</summary>
public sealed class RetrieveService(
    IDumpDownloadService downloader,
    IArchiveExtractor extractor,
    IMusicBrainzImporter importer,
    ILogger<RetrieveService> logger,
    ImportProgress progress) : IRetrieveService
{
    private readonly ImportProgress _progress = progress;

    public async Task<RetrieveResult> RunRetrieveAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting MusicBrainz retrieve job");

        var localDir = await downloader.GetOrDownloadLatestAsync(DumpFiles.TablesByArchive.Keys.ToList(), cancellationToken);
        var extractDir = Path.Combine(localDir, "extracted");

        foreach (var (archive, tables) in DumpFiles.TablesByArchive)
        {
            // Extract only the tables still missing (re-runs and partially extracted dumps)
            var missing = tables.Where(t => !File.Exists(Path.Combine(extractDir, t))).ToList();
            if (missing.Count == 0) continue;

            logger.LogInformation("Extracting {Count} tables from {Archive}", missing.Count, archive);
            // tar gives no progress: indeterminate (the core archive takes several minutes)
            _progress.Report("Extracting", null, $"{archive}: {string.Join(", ", missing.Select(Path.GetFileName))}");
            await extractor.ExtractAsync(Path.Combine(localDir, archive), extractDir, missing, cancellationToken);
        }

        var result = await importer.ImportAsync(Path.Combine(extractDir, "mbdump"), cancellationToken);
        result.DumpName = Path.GetFileName(localDir);

        logger.LogInformation(
            "MusicBrainz import {Dump} completed in {Duration}: {Genres} genres, {Relations} genre relations, {Artists} artists, {ArtistGenres} artist genres, {Links} Spotify links",
            result.DumpName, result.Duration, result.Genres, result.GenreRelations, result.Artists, result.ArtistGenres, result.SpotifyLinks);
        return result;
    }
}
