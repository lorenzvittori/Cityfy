using CityFy.Orchestrator.Models;
using CityFy.Orchestrator.Services;
using RabbitMQ.Client;
using ServiceDefault.Contracts;
using ServiceDefault.Messaging;

namespace CityFy.Orchestrator.Consumers;

/// <summary>The catalog of a run is refreshed: the run is Done (updates outside the pipeline have no run).</summary>
public sealed class UserCatalogUpdatedConsumer(IConnection connection, IServiceScopeFactory scopes, ILogger<UserCatalogUpdatedConsumer> logger)
    : MessageConsumer<UserCatalogUpdated>(connection, scopes, logger)
{
    protected override string QueueName => "orchestrator.user-catalog-updated";

    protected override async Task HandleAsync(UserCatalogUpdated message, MessageContext context, IServiceProvider services, CancellationToken ct)
    {
        if (message.RunId is null) return;

        var closed = await services.GetRequiredService<IPipelineService>()
            .CompleteAsync(message.RunId, PipelineStatus.Done, null, null, ct);

        if (closed)
            Logger.LogInformation("Run {RunId} done: catalog {Version} of {UserId} ({Placed}/{Artists} artists placed)",
                message.RunId, message.CatalogVersion, message.UserId, message.PlacedArtists, message.Artists);
        else
            Logger.LogWarning("UserCatalogUpdated for run {RunId} ignored: run not Building (timed out?)", message.RunId);
    }
}