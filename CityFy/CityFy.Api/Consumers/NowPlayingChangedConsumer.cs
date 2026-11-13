using CityFy.Api.Services;
using RabbitMQ.Client;
using ServiceDefault.Contracts;
using ServiceDefault.Messaging;

namespace CityFy.Api.Consumers;

/// <summary>The track playing for a user changed: to the user's open pages (api/v1/live).</summary>
public sealed class NowPlayingChangedConsumer(IConnection connection, IServiceScopeFactory scopes, ILogger<NowPlayingChangedConsumer> logger)
    : MessageConsumer<NowPlayingChanged>(connection, scopes, logger)
{
    protected override string QueueName => "api.now-playing-changed";

    protected override Task HandleAsync(NowPlayingChanged message, MessageContext context, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<ILiveService>().OnNowPlayingChangedAsync(message, ct);
}
