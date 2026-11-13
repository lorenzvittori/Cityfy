using CityFy.Retrieve.Dump.Spotify.Services;
using RabbitMQ.Client;
using ServiceDefault.Contracts;
using ServiceDefault.Messaging;

namespace CityFy.Retrieve.Dump.Spotify.Consumers;

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
