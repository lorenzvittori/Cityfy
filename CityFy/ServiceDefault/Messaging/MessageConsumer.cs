using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using ServiceDefault.Errors;

namespace ServiceDefault.Messaging;

public sealed record MessageContext(string MessageId, string? CorrelationId, int Retry, int MaxRetries)
{
    /// <summary>True when a failure now sends the message to the DLQ instead of retrying.</summary>
    public bool IsLastAttempt => Retry >= MaxRetries;
}

/// <summary>
/// Base class of a queue consumer. For each message: deduplication (if an
/// <see cref="IProcessedMessageStore"/> is registered), handling in its own DI scope, manual ack.
/// On failure the message is retried through the delay queues, then dead-lettered after
/// <see cref="OnFinalFailureAsync"/> has run. Malformed messages go straight to the DLQ.
/// </summary>
public abstract class MessageConsumer<T> : BackgroundService where T : class
{
    private readonly IConnection _connection;
    private readonly IServiceScopeFactory _scopeFactory;
    protected readonly ILogger Logger;
    private IChannel? _channel;

    protected MessageConsumer(IConnection connection, IServiceScopeFactory scopeFactory, ILogger logger)
    {
        _connection = connection;
        _scopeFactory = scopeFactory;
        Logger = logger;
    }

    /// <summary>Queue of this consumer, e.g. "catalog.build-city". One queue per consumer.</summary>
    protected abstract string QueueName { get; }

    /// <summary>Delay before each retry; after the last one the message is dead-lettered.</summary>
    protected virtual IReadOnlyList<TimeSpan> RetryDelays => MessagingTopology.RetryDelays;

    /// <summary>
    /// Whether a failure is worth retrying. By default a CityFyException with a 4xx status (bad input,
    /// missing data) goes straight to the final failure: retrying would fail the same way.
    /// </summary>
    protected virtual bool IsRetryable(Exception error) => error is not CityFyException { StatusCode: < 500 };

    /// <summary>Messages handled in parallel by this instance (1 = strictly one at a time).</summary>
    protected virtual ushort Prefetch => 1;

    protected abstract Task HandleAsync(T message, MessageContext context, IServiceProvider services, CancellationToken ct);

    /// <summary>Called once when the message is about to be dead-lettered (e.g. publish a "failed" event).</summary>
    protected virtual Task OnFinalFailureAsync(T message, MessageContext context, Exception error, IServiceProvider services, CancellationToken ct) =>
        Task.CompletedTask;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await DeclareTopologyAsync(_channel, stoppingToken);
        await _channel.BasicQosAsync(0, Prefetch, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (_, ea) => OnReceivedAsync(ea, stoppingToken);
        await _channel.BasicConsumeAsync(QueueName, autoAck: false, consumer, stoppingToken);

        Logger.LogInformation("Consuming {Queue} ({Type})", QueueName, MessagingTopology.RoutingKey<T>());

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // shutdown: unacked messages are redelivered to another instance / at next start
        }
    }

    private async Task DeclareTopologyAsync(IChannel ch, CancellationToken ct)
    {
        await ch.ExchangeDeclareAsync(MessagingTopology.EventsExchange, ExchangeType.Topic, durable: true, cancellationToken: ct);
        await ch.ExchangeDeclareAsync(MessagingTopology.DeadLetterExchange, ExchangeType.Direct, durable: true, cancellationToken: ct);

        await ch.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = MessagingTopology.DeadLetterExchange,
                ["x-dead-letter-routing-key"] = QueueName
            }, cancellationToken: ct);
        await ch.QueueBindAsync(QueueName, MessagingTopology.EventsExchange, MessagingTopology.RoutingKey<T>(), cancellationToken: ct);

        var dlq = MessagingTopology.DeadLetterQueue(QueueName);
        await ch.QueueDeclareAsync(dlq, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await ch.QueueBindAsync(dlq, MessagingTopology.DeadLetterExchange, QueueName, cancellationToken: ct);

        // Delay queues: no consumer, messages expire and are dead-lettered back to the main queue
        for (var i = 0; i < RetryDelays.Count; i++)
        {
            await ch.QueueDeclareAsync(MessagingTopology.RetryQueue(QueueName, i + 1), durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-message-ttl"] = (int)RetryDelays[i].TotalMilliseconds,
                    ["x-dead-letter-exchange"] = "",
                    ["x-dead-letter-routing-key"] = QueueName
                }, cancellationToken: ct);
        }
    }

    private async Task OnReceivedAsync(BasicDeliverEventArgs ea, CancellationToken stoppingToken)
    {
        var channel = _channel!;
        var props = ea.BasicProperties;
        var context = new MessageContext(props.MessageId ?? $"tag-{ea.DeliveryTag}", props.CorrelationId, ReadRetry(props), RetryDelays.Count);

        T? message;
        try
        {
            message = JsonSerializer.Deserialize<T>(ea.Body.Span, RabbitMqPublisher.JsonOptions)
                ?? throw new JsonException("null message");
        }
        catch (JsonException ex)
        {
            Logger.LogError(ex, "Malformed {Type} message {MessageId} on {Queue}: dead-lettered", MessagingTopology.RoutingKey<T>(), context.MessageId, QueueName);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, stoppingToken);
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dedup = scope.ServiceProvider.GetService<IProcessedMessageStore>();

        try
        {
            if (dedup != null && await dedup.IsProcessedAsync(QueueName, context.MessageId, stoppingToken))
            {
                Logger.LogInformation("Duplicate {MessageId} on {Queue}: skipped", context.MessageId, QueueName);
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, stoppingToken);
                return;
            }

            await HandleAsync(message, context, scope.ServiceProvider, stoppingToken);

            if (dedup != null)
                await dedup.MarkProcessedAsync(QueueName, context.MessageId, stoppingToken);
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Not acked: the broker redelivers it
        }
        catch (Exception ex)
        {
            await HandleFailureAsync(channel, ea, message, context, ex, scope.ServiceProvider, stoppingToken);
        }
    }

    private async Task HandleFailureAsync(IChannel channel, BasicDeliverEventArgs ea, T message, MessageContext context,
        Exception error, IServiceProvider services, CancellationToken ct)
    {
        if (!context.IsLastAttempt && IsRetryable(error))
        {
            var retry = context.Retry + 1;
            Logger.LogWarning(error, "{Queue}: message {MessageId} failed, retry {Retry}/{Max} in {Delay}",
                QueueName, context.MessageId, retry, RetryDelays.Count, RetryDelays[retry - 1]);

            // Copy into the delay queue (same id/correlation, incremented retry header), then ack the original
            var props = new BasicProperties(ea.BasicProperties)
            {
                Headers = new Dictionary<string, object?>(ea.BasicProperties.Headers ?? new Dictionary<string, object?>())
                {
                    [MessagingTopology.AttemptHeader] = retry
                }
            };
            await channel.BasicPublishAsync("", MessagingTopology.RetryQueue(QueueName, retry), mandatory: false, props, ea.Body, ct);
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
            return;
        }

        Logger.LogError(error, "{Queue}: message {MessageId} failed after {Retries} retries: dead-lettered",
            QueueName, context.MessageId, context.Retry);
        try
        {
            await OnFinalFailureAsync(message, context, error, services, ct);
        }
        catch (Exception hookError)
        {
            Logger.LogError(hookError, "{Queue}: final-failure handler failed for {MessageId}", QueueName, context.MessageId);
        }
        await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, ct);
    }

    private static int ReadRetry(IReadOnlyBasicProperties props)
    {
        if (props.Headers == null || !props.Headers.TryGetValue(MessagingTopology.AttemptHeader, out var value) || value == null)
            return 0;

        return value switch
        {
            int i => i,
            long l => (int)l,
            byte[] b when int.TryParse(Encoding.UTF8.GetString(b), out var parsed) => parsed,
            _ => 0
        };
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_channel != null)
            await _channel.DisposeAsync();
    }
}
