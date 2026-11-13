using CityFy.Orchestrator.Services;
using RabbitMQ.Client;
using ServiceDefault.Contracts;
using ServiceDefault.Messaging;

namespace CityFy.Orchestrator.Consumers;

/// <summary>New plays for a user: open or extend (debounce) the user's Pending run.</summary>
public sealed class ListeningImportedConsumer(IConnection connection, IServiceScopeFactory scopes, ILogger<ListeningImportedConsumer> logger)
    : MessageConsumer<ListeningImported>(connection, scopes, logger)
{
    protected override string QueueName => "orchestrator.listening-imported";

    protected override Task HandleAsync(ListeningImported message, MessageContext context, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<IPipelineService>().AddImportAsync(message, ct);
}
