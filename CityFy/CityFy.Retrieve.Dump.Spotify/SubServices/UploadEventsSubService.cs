using ServiceDefault.Contracts;
using ServiceDefault.Listening;
using ServiceDefault.Messaging;

namespace CityFy.Retrieve.Dump.Spotify.SubServices;

public sealed class UploadEventsSubService(IMessagePublisher publisher) : IUploadEventsSubService
{
    public Task PublishReceivedAsync(string uploadId, string userId, CancellationToken ct) =>
        publisher.PublishAsync(new UploadReceived(uploadId, userId), correlationId: uploadId, ct);

    public Task PublishImportedAsync(string uploadId, string userId, int newPlays, CancellationToken ct) =>
        publisher.PublishAsync(new ListeningImported(userId, ListeningSources.Dump, uploadId, newPlays, DateTime.UtcNow), correlationId: uploadId, ct);
}