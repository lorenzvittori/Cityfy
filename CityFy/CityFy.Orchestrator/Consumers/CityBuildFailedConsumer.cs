using CityFy.Orchestrator.Models;
using CityFy.Orchestrator.Services;
using RabbitMQ.Client;
using ServiceDefault.Contracts;
using ServiceDefault.Messaging;

namespace CityFy.Orchestrator.Consumers;

public sealed class CityBuildFailedConsumer(IConnection connection, IServiceScopeFactory scopes, ILogger<CityBuildFailedConsumer> logger)
    : MessageConsumer<CityBuildFailed>(connection, scopes, logger)
{
    protected override string QueueName => "orchestrator.city-build-failed";

    protected override async Task HandleAsync(CityBuildFailed message, MessageContext context, IServiceProvider services, CancellationToken ct)
    {
        await services.GetRequiredService<IPipelineService>()
            .CompleteAsync(message.RunId, PipelineStatus.Failed, message.ErrorCode, message.Error, ct);
        Logger.LogWarning("Run {RunId} failed for {UserId}: {ErrorCode} {Error}", message.RunId, message.UserId, message.ErrorCode, message.Error);
    }
}
