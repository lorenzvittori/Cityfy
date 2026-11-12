using CityFy.Retrieve.Dump.Spotify.Services;

namespace CityFy.Retrieve.Dump.Spotify.HostedServices;

/// <summary>
/// At startup, queues again the uploads left Pending or interrupted (e.g. the broker was down at
/// upload time, or the service stopped mid-import).
/// </summary>
public sealed class UploadRecoveryService(IServiceScopeFactory scopes, ILogger<UploadRecoveryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IUploadProcessor>().RequeueUnfinishedAsync(stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Upload recovery failed");
        }
    }
}