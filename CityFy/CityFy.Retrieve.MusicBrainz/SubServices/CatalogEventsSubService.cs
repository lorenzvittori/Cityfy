using ServiceDefault.Contracts;
using ServiceDefault.Messaging;

namespace CityFy.Retrieve.MusicBrainz.SubServices;

public sealed class CatalogEventsSubService(IMessagePublisher publisher) : ICatalogEventsSubService
{
    public Task PublishUpdatedAsync(string? dumpName, CancellationToken ct) =>
        publisher.PublishAsync(new CatalogUpdated(dumpName, DateTime.UtcNow), dumpName, ct);
}