using CityFy.Orchestrator.Models;
using CityFy.Orchestrator.Services;
using RabbitMQ.Client;
using ServiceDefault.Contracts;
using ServiceDefault.Messaging;

namespace CityFy.Orchestrator.Consumers;

public sealed class CatalogRefreshFailedConsumer(IConnection connection, IServiceScopeFactory scopes, ILogger<CatalogRefreshFailedConsumer> logger)
    : MessageConsumer<CatalogRefreshFailed>(connection, scopes, logger)
{
    protected override string QueueName => "orchestrator.catalog-refresh-failed";

    protected override async Task HandleAsync(CatalogRefreshFailed message, MessageContext context, IServiceProvider services, CancellationToken ct)
    {
        await services.GetRequiredService<IPipelineService>()
            .CompleteAsync(message.RunId, PipelineStatus.Failed, message.ErrorCode, message.Error, ct);
        Logger.LogWarning("Run {RunId} failed for {UserId}: {ErrorCode} {Error}", message.RunId, message.UserId, message.ErrorCode, message.Error);
    }
}