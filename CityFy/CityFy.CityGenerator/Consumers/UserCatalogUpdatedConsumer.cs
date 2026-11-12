using CityFy.CityGenerator.Services;
using RabbitMQ.Client;
using ServiceDefault.Contracts;
using ServiceDefault.Messaging;

namespace CityFy.CityGenerator.Consumers;

/// <summary>A user's catalog is ready: a user without cities gets a first one.</summary>
public sealed class UserCatalogUpdatedConsumer(IConnection connection, IServiceScopeFactory scopes, ILogger<UserCatalogUpdatedConsumer> logger)
    : MessageConsumer<UserCatalogUpdated>(connection, scopes, logger)
{
    protected override string QueueName => "city-generator.user-catalog-updated";

    protected override Task HandleAsync(UserCatalogUpdated message, MessageContext context, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<ICityGenerationService>().HandleCatalogUpdatedAsync(message, ct);
}
