using CityFy.Retrieve.MusicBrainz.Models;
using CityFy.Retrieve.MusicBrainz.SubServices;

namespace CityFy.Retrieve.MusicBrainz.Services;

/// <summary>Download the latest export, extract the needed tables, import them.</summary>
public sealed class RetrieveService(
    IDumpDownloadSubService download,
    IDumpExtractSubService extract,
    IMusicBrainzImportSubService import,
    ICatalogEventsSubService catalogEvents,
    ILogger<RetrieveService> logger) : IRetrieveService
{
    public async Task<RetrieveResult> RunRetrieveAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting MusicBrainz retrieve job");

        var localDir = await download.GetOrDownloadLatestAsync(DumpFiles.TablesByArchive.Keys.ToList(), cancellationToken);
        var tablesDir = await extract.ExtractMissingAsync(localDir, cancellationToken);
        var result = await import.ImportAsync(tablesDir, cancellationToken);
        result.DumpName = Path.GetFileName(localDir);

        logger.LogInformation(
            "MusicBrainz import {Dump} completed in {Duration}: {Genres} genres, {Relations} genre relations, {Artists} artists, {ArtistGenres} artist genres, {Links} Spotify links",
            result.DumpName, result.Duration, result.Genres, result.GenreRelations, result.Artists, result.ArtistGenres, result.SpotifyLinks);
        return result;
    }

    public async Task NotifyCatalogUpdatedAsync(RetrieveResult result, CancellationToken ct)
    {
        try
        {
            await catalogEvents.PublishUpdatedAsync(result.DumpName, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Catalog imported, but CatalogUpdated could not be published: cities are not rebuilt automatically");
        }
    }
}