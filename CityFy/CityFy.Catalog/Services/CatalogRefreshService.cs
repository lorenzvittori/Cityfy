using CityFy.Catalog.Models;
using CityFy.Catalog.SubServices;
using ServiceDefault.Contracts;
using ServiceDefault.Errors;

namespace CityFy.Catalog.Services;

public sealed class CatalogRefreshService(
    IListeningAggregationSubService listening,
    IArtistResolveSubService artists,
    IGenreHierarchySubService genres,
    IArtistPlacementSubService placement,
    IUserCatalogStoreSubService catalogs,
    ICatalogEventsSubService events,
    ICatalogRefreshErrorSubService errors,
    ILogger<CatalogRefreshService> logger) : ICatalogRefreshService
{
    public async Task<UserCatalog?> RefreshAsync(string userId, CancellationToken ct)
    {
        var plays = await listening.AggregateAsync(userId, null, null, ct);
        var resolved = await artists.ResolveAsync(plays, ct);
        var hierarchy = await genres.GetAsync(ct);
        var version = catalogs.NewVersion();
        var placements = placement.Place(userId, version, plays, resolved, hierarchy);

        // Data deleted while the refresh was queued: storing a catalog would bring it back
        if (!await listening.HasPlaysAsync(userId, ct))
        {
            logger.LogInformation("Catalog of user {UserId} not stored: the user has no plays", userId);
            return null;
        }

        var catalog = await catalogs.ReplaceAsync(userId, version, placements, DateTime.UtcNow, ct);
        logger.LogInformation("Catalog {Version} of user {UserId}: {Placed}/{Artists} artists placed",
            catalog.Version, userId, catalog.PlacedArtists, catalog.Artists);
        return catalog;
    }

    public async Task HandleRefreshRequestedAsync(RefreshCatalogRequested message, CancellationToken ct)
    {
        if (await RefreshAsync(message.UserId, ct) is { } catalog)
            await events.PublishUpdatedAsync(message.RunId, catalog, ct);
        else
            await events.PublishFailedAsync(message.RunId, message.UserId, ErrorCodes.CityNoPlays, "The user has no plays", ct);
    }

    public Task HandleRefreshFailedAsync(RefreshCatalogRequested message, Exception error, CancellationToken ct) =>
        events.PublishFailedAsync(message.RunId, message.UserId, errors.ToErrorCode(error), error.Message, ct);
}
