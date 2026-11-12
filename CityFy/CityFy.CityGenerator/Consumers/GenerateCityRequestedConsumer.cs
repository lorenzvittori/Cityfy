using CityFy.CityGenerator.Services;
using RabbitMQ.Client;
using ServiceDefault.Contracts;
using ServiceDefault.Messaging;

namespace CityFy.CityGenerator.Consumers;

/// <summary>Runs a generation; after the last retry the city is marked Failed with its error code.</summary>
public sealed class GenerateCityRequestedConsumer(IConnection connection, IServiceScopeFactory scopes, ILogger<GenerateCityRequestedConsumer> logger)
    : MessageConsumer<GenerateCityRequested>(connection, scopes, logger)
{
    protected override string QueueName => "city-generator.generate-city";

    protected override Task HandleAsync(GenerateCityRequested message, MessageContext context, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<ICityGenerationService>().GenerateAsync(message.CityId, ct);

    protected override Task OnFinalFailureAsync(GenerateCityRequested message, MessageContext context, Exception error, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<ICityGenerationService>().MarkFailedAsync(message.CityId, error, ct);
}
