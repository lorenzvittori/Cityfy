using CityFy.Retrieve.LastFm.Services;

namespace CityFy.Retrieve.LastFm.HostedServices;

/// <summary>
/// At startup, queues again the history imports left Pending (the queue is in memory) or InProgress
/// (interrupted by a shutdown): they resume from the page saved on the account.
/// </summary>
public sealed class LastFmImportRecoveryService(IServiceScopeFactory scopes, ILogger<LastFmImportRecoveryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ILastFmImportService>().RequeueUnfinishedAsync(stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Last.fm import recovery failed");
        }
    }
}
