using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace ServiceDefault.Messaging;

/// <summary>
/// One channel with publisher confirms, shared and serialized (a channel is not safe for
/// concurrent publishes). Tracing headers are added by RabbitMQ.Client 7 itself.
/// </summary>
public sealed class RabbitMqPublisher(IConnection connection, ILogger<RabbitMqPublisher> logger) : IMessagePublisher, IAsyncDisposable
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private IChannel? _channel;

    public async Task PublishAsync<T>(T message, string? correlationId = null, CancellationToken ct = default) where T : class
    {
        var routingKey = MessagingTopology.RoutingKey<T>();
        var props = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            Type = routingKey,
            MessageId = Guid.NewGuid().ToString("N"),
            CorrelationId = correlationId,
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        };
        var body = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);

        await _lock.WaitAsync(ct);
        try
        {
            var channel = await GetChannelAsync(ct);
            // With confirmation tracking this completes when the broker confirms (throws on nack)
            await channel.BasicPublishAsync(MessagingTopology.EventsExchange, routingKey, mandatory: false, props, body, ct);
        }
        catch
        {
            // Drop a possibly broken channel: the next publish opens a new one
            await ResetChannelAsync();
            throw;
        }
        finally
        {
            _lock.Release();
        }

        logger.LogDebug("Published {Type} {MessageId} (correlation {CorrelationId})", routingKey, props.MessageId, correlationId);
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true })
            return _channel;

        _channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), ct);
        await _channel.ExchangeDeclareAsync(MessagingTopology.EventsExchange, ExchangeType.Topic, durable: true, cancellationToken: ct);
        return _channel;
    }

    private async Task ResetChannelAsync()
    {
        if (_channel == null) return;
        try { await _channel.DisposeAsync(); } catch { /* already broken */ }
        _channel = null;
    }

    public async ValueTask DisposeAsync()
    {
        await ResetChannelAsync();
        _lock.Dispose();
    }
}
