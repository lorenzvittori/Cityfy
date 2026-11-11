using CityFy.Retrieve.Dump.Spotify.Services;
using RabbitMQ.Client;
using ServiceDefault.Contracts;
using ServiceDefault.Messaging;

namespace CityFy.Retrieve.Dump.Spotify.HostedServices;

/// <summary>Imports an uploaded export when its UploadReceived event arrives (replaces the 1-minute polling).</summary>
public sealed class UploadReceivedConsumer(IConnection connection, IServiceScopeFactory scopes, ILogger<UploadReceivedConsumer> logger)
    : MessageConsumer<UploadReceived>(connection, scopes, logger)
{
    protected override string QueueName => "dump-spotify.upload-received";

    protected override Task HandleAsync(UploadReceived message, MessageContext context, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<IUploadProcessor>().ProcessAsync(message.UploadId, ct);

    protected override Task OnFinalFailureAsync(UploadReceived message, MessageContext context, Exception error, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<IUploadProcessor>().MarkFailedAsync(message.UploadId, error);
}

/// <summary>
/// At startup, re-publishes UploadReceived for the uploads left Pending or interrupted
/// (e.g. the broker was down at upload time, or the service stopped mid-import).
/// Duplicates are harmless: the processor skips finished uploads and the import is idempotent.
/// </summary>
public sealed class UploadRecoveryService(IServiceScopeFactory scopes, ILogger<UploadRecoveryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var persistence = scope.ServiceProvider.GetRequiredService<IStreamingBlPersistenceService>();
            var publisher = scope.ServiceProvider.GetRequiredService<IMessagePublisher>();

            var unfinished = (await persistence.GetTasksByStatusAsync(ServiceDefault.Models.ProcessingStatus.Pending))
                .Concat(await persistence.GetTasksByStatusAsync(ServiceDefault.Models.ProcessingStatus.WORKING_IN_PROGRESS))
                .Where(t => !string.IsNullOrWhiteSpace(t.UserId))
                .ToList();

            foreach (var task in unfinished)
                await publisher.PublishAsync(new UploadReceived(task.UploadId, task.UserId!), task.UploadId, stoppingToken);

            if (unfinished.Count > 0)
                logger.LogInformation("Re-queued {Count} unfinished uploads", unfinished.Count);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Upload recovery failed");
        }
    }
}
