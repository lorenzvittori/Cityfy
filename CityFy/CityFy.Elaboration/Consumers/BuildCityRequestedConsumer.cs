using CityFy.Elaboration.Services;
using RabbitMQ.Client;
using ServiceDefault.Contracts;
using ServiceDefault.Errors;
using ServiceDefault.Messaging;

namespace CityFy.Elaboration.Consumers;

/// <summary>
/// Builds the city for every requested period, then answers CityBuilt. Failures are retried
/// by the messaging layer; after the last retry CityBuildFailed is published.
/// </summary>
public sealed class BuildCityRequestedConsumer(IConnection connection, IServiceScopeFactory scopes, ILogger<BuildCityRequestedConsumer> logger)
    : MessageConsumer<BuildCityRequested>(connection, scopes, logger)
{
    protected override string QueueName => "elaboration.build-city";

    protected override async Task HandleAsync(BuildCityRequested message, MessageContext context, IServiceProvider services, CancellationToken ct)
    {
        var cities = services.GetRequiredService<ICityService>();

        var periods = new List<string>();
        foreach (var raw in message.Periods)
        {
            if (!Periods.TryNormalize(raw, out var period))
            {
                Logger.LogWarning("Run {RunId}: invalid period '{Period}' skipped", message.RunId, raw);
                continue;
            }

            await cities.BuildAsync(message.UserId, period, ct);
            periods.Add(period);
        }

        await services.GetRequiredService<IMessagePublisher>()
            .PublishAsync(new CityBuilt(message.RunId, message.UserId, periods, DateTime.UtcNow), message.RunId, ct);
    }

    protected override Task OnFinalFailureAsync(BuildCityRequested message, MessageContext context, Exception error, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<IMessagePublisher>()
            .PublishAsync(new CityBuildFailed(message.RunId, message.UserId, ToErrorCode(error), error.Message), message.RunId, ct);

    /// <summary>MusicBrainz down/unreachable is told apart: the user can only wait, nothing is wrong with their data.</summary>
    internal static string ToErrorCode(Exception error) => error switch
    {
        CityFyException c => c.Code,
        HttpRequestException or TaskCanceledException => ErrorCodes.MusicBrainzUnavailable,
        Polly.CircuitBreaker.BrokenCircuitException or Polly.Timeout.TimeoutRejectedException => ErrorCodes.MusicBrainzUnavailable,
        _ => ErrorCodes.CityBuildFailed
    };
}
