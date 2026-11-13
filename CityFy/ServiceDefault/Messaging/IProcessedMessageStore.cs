namespace ServiceDefault.Messaging;

/// <summary>
/// Consumer-side deduplication: RabbitMQ delivers at least once (e.g. redelivery after a
/// crash before the ack), so a consumer records the message ids it already handled.
/// </summary>
public interface IProcessedMessageStore
{
    Task<bool> IsProcessedAsync(string consumer, string messageId, CancellationToken ct);
    Task MarkProcessedAsync(string consumer, string messageId, CancellationToken ct);
}
