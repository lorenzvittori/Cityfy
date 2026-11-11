using CityFy.Retrieve.Spotify.Options;
using CityFy.Retrieve.Spotify.Repositories;
using CityFy.Retrieve.Spotify.Services;
using Microsoft.Extensions.Options;

namespace CityFy.Retrieve.Spotify.HostedServices;

/// <summary>
/// Queues a retrieve for every linked account every Spotify:ScheduledRetrieveMinutes.
/// The first round starts one interval after startup.
/// </summary>
public sealed class SpotifyRetrieveScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISpotifyAccountRepository _accounts;
    private readonly SpotifyOptions _options;
    private readonly ILogger<SpotifyRetrieveScheduler> _logger;

    public SpotifyRetrieveScheduler(IServiceScopeFactory scopeFactory, ISpotifyAccountRepository accounts,
        IOptions<SpotifyOptions> options, ILogger<SpotifyRetrieveScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _accounts = accounts;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.ScheduledRetrieveMinutes <= 0)
        {
            _logger.LogInformation("Scheduled Spotify retrieve disabled");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.ScheduledRetrieveMinutes));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    var userIds = await _accounts.GetAllUserIdsAsync(stoppingToken);
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var retrieve = scope.ServiceProvider.GetRequiredService<ISpotifyRetrieveService>();

                    foreach (var userId in userIds)
                        await retrieve.EnqueueRetrieveAsync(userId, stoppingToken);

                    _logger.LogInformation("Scheduled retrieve queued for {Count} accounts", userIds.Count);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Scheduled Spotify retrieve round failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
    }
}
