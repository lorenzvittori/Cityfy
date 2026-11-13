using ServiceDefault.Contracts;
using ServiceDefault.Messaging;

namespace CityFy.Api.SubServices;

public sealed class UserActivityEventSubService(IMessagePublisher publisher) : IUserActivityEventSubService
{
    public Task PublishActiveAsync(string userId, CancellationToken ct) =>
        publisher.PublishAsync(new UserActive(userId, DateTime.UtcNow), correlationId: userId, ct);
}
