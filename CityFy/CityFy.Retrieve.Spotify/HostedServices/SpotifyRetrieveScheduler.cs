using CityFy.Retrieve.Spotify.Options;
using CityFy.Retrieve.Spotify.Services;
using Microsoft.Extensions.Options;

namespace CityFy.Retrieve.Spotify.HostedServices;

/// <summary>
/// Queues a retrieve for every linked account every Spotify:ScheduledRetrieveMinutes.
/// The first round starts one interval after startup.
/// </summary>
public sealed class SpotifyRetrieveScheduler(
    IServiceScopeFactory scopeFactory,
    IOptions<SpotifyOptions> options,
    ILogger<SpotifyRetrieveScheduler> logger) : BackgroundService
{
    private readonly SpotifyOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.ScheduledRetrieveMinutes <= 0)
        {
            logger.LogInformation("Scheduled Spotify retrieve disabled");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.ScheduledRetrieveMinutes));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var accounts = scope.ServiceProvider.GetRequiredService<ISpotifyAccountService>();
                    var retrieve = scope.ServiceProvider.GetRequiredService<ISpotifyRetrieveService>();

                    var userIds = await accounts.GetAllUserIdsAsync(stoppingToken);
                    foreach (var userId in userIds)
                        await retrieve.EnqueueRetrieveAsync(userId, stoppingToken);

                    logger.LogInformation("Scheduled retrieve queued for {Count} accounts", userIds.Count);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Scheduled Spotify retrieve round failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
    }
}
