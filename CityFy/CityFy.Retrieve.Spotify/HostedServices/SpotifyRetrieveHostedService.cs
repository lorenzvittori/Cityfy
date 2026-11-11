using CityFy.Retrieve.Spotify.Services;

namespace CityFy.Retrieve.Spotify.HostedServices
{
    // Consumes queued retrieve jobs one at a time. Each job runs in its own DI scope
    // so it gets fresh typed HttpClients from IHttpClientFactory.
    public class SpotifyRetrieveHostedService : BackgroundService
    {
        private readonly IRetrieveJobQueue _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SpotifyRetrieveHostedService> _logger;

        public SpotifyRetrieveHostedService(
            IRetrieveJobQueue queue,
            IServiceScopeFactory scopeFactory,
            ILogger<SpotifyRetrieveHostedService> logger)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("SpotifyRetrieveHostedService started");

            try
            {
                await foreach (var job in _queue.ReadAllAsync(stoppingToken))
                {
                    using var scope = _scopeFactory.CreateScope();
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
                        // Already logged and recorded as FAILED by the service; keep the worker alive.
                        _logger.LogDebug(ex, "Job {JobId} ended with an error", job.JobId);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // normal shutdown
            }

            _logger.LogInformation("SpotifyRetrieveHostedService stopping");
        }
    }
}
