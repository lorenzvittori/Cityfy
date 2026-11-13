using ServiceDefault.Contracts;
using ServiceDefault.Listening;
using ServiceDefault.Messaging;

namespace CityFy.Retrieve.LastFm.SubServices;

public sealed class ListeningImportedEventSubService(IMessagePublisher publisher) : IListeningImportedEventSubService
{
    public Task PublishAsync(string userId, string sourceRef, int newPlays, CancellationToken ct) =>
        publisher.PublishAsync(new ListeningImported(userId, ListeningSources.LastFm, sourceRef, newPlays, DateTime.UtcNow),
            correlationId: sourceRef, ct);
}
