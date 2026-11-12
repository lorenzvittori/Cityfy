using CityFy.Orchestrator.Models;
using CityFy.Orchestrator.Services;
using RabbitMQ.Client;
using ServiceDefault.Contracts;
using ServiceDefault.Messaging;

namespace CityFy.Orchestrator.Consumers;

public sealed class CityBuiltConsumer(IConnection connection, IServiceScopeFactory scopes, ILogger<CityBuiltConsumer> logger)
    : MessageConsumer<CityBuilt>(connection, scopes, logger)
{
    protected override string QueueName => "orchestrator.city-built";

    protected override async Task HandleAsync(CityBuilt message, MessageContext context, IServiceProvider services, CancellationToken ct)
    {
        var closed = await services.GetRequiredService<IPipelineService>()
            .CompleteAsync(message.RunId, PipelineStatus.Done, null, null, ct);

        if (closed)
            Logger.LogInformation("Run {RunId} done: city of {UserId} built for {Periods}", message.RunId, message.UserId, string.Join(", ", message.Periods));
        else
            Logger.LogWarning("CityBuilt for run {RunId} ignored: run not Building (timed out?)", message.RunId);
    }
}
