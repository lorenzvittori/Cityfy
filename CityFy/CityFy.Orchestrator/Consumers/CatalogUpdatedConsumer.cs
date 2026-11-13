using CityFy.Orchestrator.Services;
using RabbitMQ.Client;
using ServiceDefault.Contracts;
using ServiceDefault.Messaging;

namespace CityFy.Orchestrator.Consumers;

/// <summary>New genre catalog: every user's city may change, rebuild them all (normal debounce per user).</summary>
public sealed class CatalogUpdatedConsumer(IConnection connection, IServiceScopeFactory scopes, ILogger<CatalogUpdatedConsumer> logger)
    : MessageConsumer<CatalogUpdated>(connection, scopes, logger)
{
    protected override string QueueName => "orchestrator.catalog-updated";

    protected override Task HandleAsync(CatalogUpdated message, MessageContext context, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<IPipelineService>().RebuildAllAsync(message, ct);
}
