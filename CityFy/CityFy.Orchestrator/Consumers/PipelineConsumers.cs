using CityFy.Orchestrator.Models;
using CityFy.Orchestrator.Options;
using CityFy.Orchestrator.Repositories;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using ServiceDefault.Contracts;
using ServiceDefault.Messaging;

namespace CityFy.Orchestrator.Consumers;

/// <summary>New plays for a user: open or extend (debounce) the user's Pending run.</summary>
public sealed class ListeningImportedConsumer(IConnection connection, IServiceScopeFactory scopes, ILogger<ListeningImportedConsumer> logger)
    : MessageConsumer<ListeningImported>(connection, scopes, logger)
{
    protected override string QueueName => "orchestrator.listening-imported";

    protected override async Task HandleAsync(ListeningImported message, MessageContext context, IServiceProvider services, CancellationToken ct)
    {
        if (message.NewPlays <= 0)
            return; // nothing new: the city would not change

        var options = services.GetRequiredService<IOptions<PipelineOptions>>().Value;
        var repo = services.GetRequiredService<PipelineRunRepository>();

        var run = await repo.AddTriggerAsync(
            message.UserId,
            new PipelineTrigger { Source = message.Source, SourceRef = message.SourceRef, NewPlays = message.NewPlays, At = DateTime.UtcNow },
            options.Periods,
            TimeSpan.FromSeconds(options.DebounceSeconds),
            TimeSpan.FromSeconds(options.MaxWaitSeconds),
            ct);

        Logger.LogInformation("User {UserId}: {NewPlays} new plays from {Source} -> run {RunId} due at {DueAt:O}",
            message.UserId, message.NewPlays, message.Source, run.RunId, run.DueAt);
    }
}

/// <summary>New genre catalog: every user's city may change, rebuild them all (normal debounce per user).</summary>
public sealed class CatalogUpdatedConsumer(IConnection connection, IServiceScopeFactory scopes, ILogger<CatalogUpdatedConsumer> logger)
    : MessageConsumer<CatalogUpdated>(connection, scopes, logger)
{
    protected override string QueueName => "orchestrator.catalog-updated";

    protected override async Task HandleAsync(CatalogUpdated message, MessageContext context, IServiceProvider services, CancellationToken ct)
    {
        var options = services.GetRequiredService<IOptions<PipelineOptions>>().Value;
        var repo = services.GetRequiredService<PipelineRunRepository>();

        // Users = whoever has plays (the cities to rebuild)
        using var cursor = await services.GetRequiredService<MongoDB.Driver.IMongoDatabase>()
            .GetCollection<ServiceDefault.Listening.ListeningEvent>(ServiceDefault.Listening.ListeningEvent.CollectionName)
            .DistinctAsync<string>(nameof(ServiceDefault.Listening.ListeningEvent.UserId),
                MongoDB.Driver.Builders<ServiceDefault.Listening.ListeningEvent>.Filter.Empty, cancellationToken: ct);
        var users = await MongoDB.Driver.IAsyncCursorExtensions.ToListAsync(cursor, ct);

        foreach (var userId in users)
            await repo.AddTriggerAsync(userId,
                new PipelineTrigger { Source = "catalog", SourceRef = message.DumpName ?? "musicbrainz", NewPlays = 0, At = DateTime.UtcNow },
                options.Periods, TimeSpan.FromSeconds(options.DebounceSeconds), TimeSpan.FromSeconds(options.MaxWaitSeconds), ct);

        Logger.LogInformation("Genre catalog {Dump} updated: rebuild queued for {Count} users", message.DumpName, users.Count);
    }
}

public sealed class CityBuiltConsumer(IConnection connection, IServiceScopeFactory scopes, ILogger<CityBuiltConsumer> logger)
    : MessageConsumer<CityBuilt>(connection, scopes, logger)
{
    protected override string QueueName => "orchestrator.city-built";

    protected override async Task HandleAsync(CityBuilt message, MessageContext context, IServiceProvider services, CancellationToken ct)
    {
        var closed = await services.GetRequiredService<PipelineRunRepository>()
            .CompleteAsync(message.RunId, PipelineStatus.Done, null, null, DateTime.UtcNow, ct);

        if (closed)
            Logger.LogInformation("Run {RunId} done: city of {UserId} built for {Periods}", message.RunId, message.UserId, string.Join(", ", message.Periods));
        else
            Logger.LogWarning("CityBuilt for run {RunId} ignored: run not Building (timed out?)", message.RunId);
    }
}

public sealed class CityBuildFailedConsumer(IConnection connection, IServiceScopeFactory scopes, ILogger<CityBuildFailedConsumer> logger)
    : MessageConsumer<CityBuildFailed>(connection, scopes, logger)
{
    protected override string QueueName => "orchestrator.city-build-failed";

    protected override async Task HandleAsync(CityBuildFailed message, MessageContext context, IServiceProvider services, CancellationToken ct)
    {
        await services.GetRequiredService<PipelineRunRepository>()
            .CompleteAsync(message.RunId, PipelineStatus.Failed, message.ErrorCode, message.Error, DateTime.UtcNow, ct);
        Logger.LogWarning("Run {RunId} failed for {UserId}: {ErrorCode} {Error}", message.RunId, message.UserId, message.ErrorCode, message.Error);
    }
}
