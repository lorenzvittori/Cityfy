using ServiceDefault.Contracts;
using ServiceDefault.Messaging;

namespace CityFy.Elaboration.SubServices;

public sealed class CityBuildEventsSubService(IMessagePublisher publisher) : ICityBuildEventsSubService
{
    public Task PublishBuiltAsync(string runId, string userId, IReadOnlyList<string> periods, CancellationToken ct) =>
        publisher.PublishAsync(new CityBuilt(runId, userId, periods, DateTime.UtcNow), runId, ct);

    public Task PublishFailedAsync(string runId, string userId, string errorCode, string error, CancellationToken ct) =>
        publisher.PublishAsync(new CityBuildFailed(runId, userId, errorCode, error), runId, ct);
}
