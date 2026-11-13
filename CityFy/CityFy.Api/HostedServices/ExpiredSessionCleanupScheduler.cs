using CityFy.Api.Options;
using CityFy.Api.Services;
using Microsoft.Extensions.Options;

namespace CityFy.Api.HostedServices;

/// <summary>
/// Every Sessions:CleanupIntervalMinutes deletes the data of the expired anonymous sessions and retries
/// the moves of anonymous data to an account that did not complete at login. The first
/// round runs one interval after startup; a failed round is logged and retried at the next one.
/// </summary>
public sealed class ExpiredSessionCleanupScheduler(
    IServiceScopeFactory scopeFactory,
    IOptions<AnonymousSessionOptions> options,
    ILogger<ExpiredSessionCleanupScheduler> logger) : BackgroundService
{
    private readonly AnonymousSessionOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.CleanupIntervalMinutes <= 0)
        {
            logger.LogInformation("Cleanup of expired anonymous sessions disabled");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.CleanupIntervalMinutes));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IAnonymousSessionService>().CleanupExpiredAsync(stoppingToken);
                    // Moves of anonymous data to an account left incomplete at login
                    await scope.ServiceProvider.GetRequiredService<IAccountService>().RetryTransfersAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Cleanup round of expired anonymous sessions failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
    }
}
