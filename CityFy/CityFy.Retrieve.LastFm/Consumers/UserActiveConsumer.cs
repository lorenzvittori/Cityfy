using CityFy.Retrieve.LastFm.Services;
using RabbitMQ.Client;
using ServiceDefault.Contracts;
using ServiceDefault.Messaging;

namespace CityFy.Retrieve.LastFm.Consumers;

/// <summary>
/// A CityFy page of the user is open: read their plays at the fast cadence. A message older than a
/// few minutes (e.g. queued while this service was down) describes a page probably closed: dropped.
/// </summary>
public sealed class UserActiveConsumer(IConnection connection, IServiceScopeFactory scopes, ILogger<UserActiveConsumer> logger)
    : MessageConsumer<UserActive>(connection, scopes, logger)
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(3);

    protected override string QueueName => "retrieve-lastfm.user-active";

    protected override Task HandleAsync(UserActive message, MessageContext context, IServiceProvider services, CancellationToken ct) =>
        DateTime.UtcNow - message.At > MaxAge
            ? Task.CompletedTask
            : services.GetRequiredService<ILastFmAccountService>().MarkActiveAsync(message.UserId, message.At, ct);
}
