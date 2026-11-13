using CityFy.Catalog.Models;
using ServiceDefault.Contracts;
using ServiceDefault.Messaging;

namespace CityFy.Catalog.SubServices;

public sealed class CatalogEventsSubService(IMessagePublisher publisher) : ICatalogEventsSubService
{
    public Task PublishUpdatedAsync(string? runId, UserCatalog catalog, CancellationToken ct) =>
        publisher.PublishAsync(new UserCatalogUpdated(runId, catalog.UserId, catalog.Version, catalog.Artists, catalog.PlacedArtists, catalog.UpdatedAt),
            runId ?? catalog.Version, ct);

    public Task PublishFailedAsync(string runId, string userId, string errorCode, string error, CancellationToken ct) =>
        publisher.PublishAsync(new CatalogRefreshFailed(runId, userId, errorCode, error), runId, ct);
}
