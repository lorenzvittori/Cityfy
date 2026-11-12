using CityFy.Elaboration.Services;
using RabbitMQ.Client;
using ServiceDefault.Contracts;
using ServiceDefault.Messaging;

namespace CityFy.Elaboration.Consumers;

/// <summary>
/// Builds the city for every requested period, then answers CityBuilt. Failures are retried
/// by the messaging layer; after the last retry CityBuildFailed is published.
/// </summary>
public sealed class BuildCityRequestedConsumer(IConnection connection, IServiceScopeFactory scopes, ILogger<BuildCityRequestedConsumer> logger)
    : MessageConsumer<BuildCityRequested>(connection, scopes, logger)
{
    protected override string QueueName => "elaboration.build-city";

    protected override Task HandleAsync(BuildCityRequested message, MessageContext context, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<ICityService>().HandleBuildRequestedAsync(message, ct);

    protected override Task OnFinalFailureAsync(BuildCityRequested message, MessageContext context, Exception error, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<ICityService>().HandleBuildFailedAsync(message, error, ct);
}
