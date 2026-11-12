using CityFy.Retrieve.Spotify.Services;

namespace CityFy.Retrieve.Spotify.HostedServices;

// Consumes queued retrieve jobs one at a time. Each job runs in its own DI scope
// so it gets fresh typed HttpClients from IHttpClientFactory.
public sealed class SpotifyRetrieveHostedService(
    IRetrieveJobQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<SpotifyRetrieveHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("SpotifyRetrieveHostedService started");

        try
        {
            await foreach (var job in queue.ReadAllAsync(stoppingToken))
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ISpotifyRetrieveService>();

                try
                {
                    await service.RunRetrieveAsync(job, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Already logged and recorded as Failed by the service; keep the worker alive.
                    logger.LogDebug(ex, "Job {JobId} ended with an error", job.JobId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // normal shutdown
        }

        logger.LogInformation("SpotifyRetrieveHostedService stopping");
    }
}
