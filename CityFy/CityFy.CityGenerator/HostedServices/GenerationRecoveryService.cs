using CityFy.CityGenerator.Services;

namespace CityFy.CityGenerator.HostedServices;

/// <summary>
/// At startup, queues again the cities left Pending or interrupted (the broker was down at request
/// time, or the service stopped mid-generation).
/// </summary>
public sealed class GenerationRecoveryService(IServiceScopeFactory scopes, ILogger<GenerationRecoveryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ICityGenerationService>().RequeueUnfinishedAsync(stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Generation recovery failed");
        }
    }
}
