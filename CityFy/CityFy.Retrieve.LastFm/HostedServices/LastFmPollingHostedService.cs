using CityFy.Retrieve.LastFm.Options;
using CityFy.Retrieve.LastFm.Services;
using Microsoft.Extensions.Options;

namespace CityFy.Retrieve.LastFm.HostedServices;

/// <summary>
/// Every LastFm:PollTickSeconds reads the accounts whose turn has come (each account has its own
/// NextPollAt, see the cadence). A failed round is logged and does not stop the loop.
/// </summary>
public sealed class LastFmPollingHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<LastFmOptions> options,
    ILogger<LastFmPollingHostedService> logger) : BackgroundService
{
    private readonly LastFmOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.PollTickSeconds <= 0)
        {
            logger.LogInformation("Last.fm polling disabled");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollTickSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<ILastFmPollService>().PollDueAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Last.fm polling round failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
    }
}
