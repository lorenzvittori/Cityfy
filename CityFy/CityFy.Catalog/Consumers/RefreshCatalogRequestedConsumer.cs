using CityFy.Catalog.Services;
using RabbitMQ.Client;
using ServiceDefault.Contracts;
using ServiceDefault.Messaging;

namespace CityFy.Catalog.Consumers;

/// <summary>
/// Rebuilds the user's artist catalog, then answers UserCatalogUpdated. Failures are retried by the
/// messaging layer; after the last retry CatalogRefreshFailed is published.
/// </summary>
public sealed class RefreshCatalogRequestedConsumer(IConnection connection, IServiceScopeFactory scopes, ILogger<RefreshCatalogRequestedConsumer> logger)
    : MessageConsumer<RefreshCatalogRequested>(connection, scopes, logger)
{
    protected override string QueueName => "catalog.refresh-catalog";

    protected override Task HandleAsync(RefreshCatalogRequested message, MessageContext context, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<ICatalogRefreshService>().HandleRefreshRequestedAsync(message, ct);

    protected override Task OnFinalFailureAsync(RefreshCatalogRequested message, MessageContext context, Exception error, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<ICatalogRefreshService>().HandleRefreshFailedAsync(message, error, ct);
}