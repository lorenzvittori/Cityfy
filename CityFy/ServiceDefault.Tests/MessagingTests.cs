using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using ServiceDefault.Messaging;
using Testcontainers.RabbitMq;

namespace ServiceDefault.Tests;

public sealed record TestMessage(string Text);

/// <summary>Collects what the test consumers see.</summary>
public sealed class Recorder
{
    public ConcurrentQueue<(string Text, int Retry, string? CorrelationId)> Handled { get; } = new();
    public TaskCompletionSource<string> Succeeded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<Exception> FinalFailure { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int FailuresBeforeSuccess { get; set; }
}

/// <summary>Fails the first N attempts, then succeeds. Short retry delays to keep the test fast.</summary>
public sealed class TestConsumer(IConnection connection, IServiceScopeFactory scopes, string queue)
    : MessageConsumer<TestMessage>(connection, scopes, NullLogger.Instance)
{
    protected override string QueueName => queue;

    protected override IReadOnlyList<TimeSpan> RetryDelays => [TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200)];

    protected override Task HandleAsync(TestMessage message, MessageContext context, IServiceProvider services, CancellationToken ct)
    {
        var recorder = services.GetRequiredService<Recorder>();
        recorder.Handled.Enqueue((message.Text, context.Retry, context.CorrelationId));
        if (recorder.FailuresBeforeSuccess < 0)
            throw new ServiceDefault.Errors.CityFyException("TEST_BAD_INPUT", "bad input"); // 400: not retryable
        if (context.Retry < recorder.FailuresBeforeSuccess)
            throw new InvalidOperationException($"failure #{context.Retry + 1}");
        recorder.Succeeded.TrySetResult(message.Text);
        return Task.CompletedTask;
    }

    protected override Task OnFinalFailureAsync(TestMessage message, MessageContext context, Exception error, IServiceProvider services, CancellationToken ct)
    {
        services.GetRequiredService<Recorder>().FinalFailure.TrySetResult(error);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Publisher + consumer base on a real RabbitMQ (Testcontainers): delivery, retry through the
/// delay queues, dead-lettering. Skipped when Docker is not available.
/// </summary>
public sealed class MessagingTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private RabbitMqContainer? _container;
    private IConnection? _connection;
    private string? _dockerUnavailable;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new RabbitMqBuilder("rabbitmq:4-management").Build();
            await _container.StartAsync();
            _connection = await new ConnectionFactory { Uri = new Uri(_container.GetConnectionString()) }.CreateConnectionAsync();
        }
        catch (Exception ex)
        {
            _dockerUnavailable = ex.Message;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection != null) await _connection.DisposeAsync();
        if (_container != null) await _container.DisposeAsync();
    }

    private async Task<(TestConsumer Consumer, Recorder Recorder, RabbitMqPublisher Publisher)> StartAsync(string queue, int failuresBeforeSuccess)
    {
        Assert.SkipWhen(_dockerUnavailable != null, $"Docker not available: {_dockerUnavailable}");

        var recorder = new Recorder { FailuresBeforeSuccess = failuresBeforeSuccess };
        var services = new ServiceCollection().AddSingleton(recorder).BuildServiceProvider();
        var consumer = new TestConsumer(_connection!, services.GetRequiredService<IServiceScopeFactory>(), queue);
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        // Wait until the queue exists and is consumed, otherwise the first publish is unroutable
        await WaitForConsumerAsync(queue);

        return (consumer, recorder, new RabbitMqPublisher(_connection!, NullLogger<RabbitMqPublisher>.Instance));
    }

    private async Task WaitForConsumerAsync(string queue)
    {
        var ct = TestContext.Current.CancellationToken;
        for (var i = 0; i < 100; i++)
        {
            try
            {
                // A passive check on a missing queue closes the channel: use a fresh one each time
                await using var probe = await _connection!.CreateChannelAsync(cancellationToken: ct);
                if (await probe.ConsumerCountAsync(queue, ct) > 0) return;
            }
            catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
            {
                // queue not declared yet
            }
            await Task.Delay(100, ct);
        }
        throw new TimeoutException($"No consumer on {queue}");
    }

    [Fact]
    public async Task PublishedMessage_IsDelivered_WithItsCorrelationId()
    {
        var (consumer, recorder, publisher) = await StartAsync("test.delivery", failuresBeforeSuccess: 0);

        await publisher.PublishAsync(new TestMessage("hello"), correlationId: "run-1", TestContext.Current.CancellationToken);

        Assert.Equal("hello", await recorder.Succeeded.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken));
        var (_, retry, correlation) = Assert.Single(recorder.Handled);
        Assert.Equal(0, retry);
        Assert.Equal("run-1", correlation);
        await consumer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task FailingMessage_IsRetried_ThenSucceeds()
    {
        var (consumer, recorder, publisher) = await StartAsync("test.retry", failuresBeforeSuccess: 2);

        await publisher.PublishAsync(new TestMessage("flaky"), ct: TestContext.Current.CancellationToken);

        await recorder.Succeeded.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal([0, 1, 2], recorder.Handled.Select(h => h.Retry));
        Assert.False(recorder.FinalFailure.Task.IsCompleted);
        await consumer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task NonTransientError_IsNotRetried()
    {
        // FailuresBeforeSuccess = -1 makes the test consumer throw a 4xx CityFyException
        var (consumer, recorder, publisher) = await StartAsync("test.no-retry", failuresBeforeSuccess: -1);

        await publisher.PublishAsync(new TestMessage("bad input"), ct: TestContext.Current.CancellationToken);

        var error = await recorder.FinalFailure.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.IsType<ServiceDefault.Errors.CityFyException>(error);
        Assert.Single(recorder.Handled);
        await consumer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task AlwaysFailingMessage_CallsFinalFailure_AndEndsInTheDlq()
    {
        var (consumer, recorder, publisher) = await StartAsync("test.dlq", failuresBeforeSuccess: int.MaxValue);

        await publisher.PublishAsync(new TestMessage("poison"), ct: TestContext.Current.CancellationToken);

        var error = await recorder.FinalFailure.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Contains("failure #3", error.Message); // first attempt + 2 retries

        await using var channel = await _connection!.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        uint inDlq = 0;
        for (var i = 0; i < 50 && inDlq == 0; i++)
        {
            inDlq = await channel.MessageCountAsync(MessagingTopology.DeadLetterQueue("test.dlq"), TestContext.Current.CancellationToken);
            if (inDlq == 0) await Task.Delay(100, TestContext.Current.CancellationToken);
        }
        Assert.Equal(1u, inDlq);
        await consumer.StopAsync(CancellationToken.None);
    }
}
