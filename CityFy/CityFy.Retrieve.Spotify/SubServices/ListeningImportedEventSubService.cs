using CityFy.Retrieve.Spotify.Models;
using ServiceDefault.Contracts;
using ServiceDefault.Listening;
using ServiceDefault.Messaging;

namespace CityFy.Retrieve.Spotify.SubServices;

public sealed class ListeningImportedEventSubService(IMessagePublisher publisher) : IListeningImportedEventSubService
{
    public Task PublishAsync(RetrieveJob job, int newPlays, CancellationToken ct) =>
        publisher.PublishAsync(
            new ListeningImported(job.UserId, ListeningSources.Api, job.JobId, newPlays, DateTime.UtcNow),
            correlationId: job.JobId, ct);
}