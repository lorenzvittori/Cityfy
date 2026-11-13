using CityFy.Retrieve.LastFm.Services;

namespace CityFy.Retrieve.LastFm.HostedServices;

/// <summary>Runs the queued history imports one at a time, each in its own DI scope.</summary>
public sealed class LastFmImportHostedService(
    ILastFmImportQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<LastFmImportHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var job in queue.ReadAllAsync(stoppingToken))
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                try
                {
                    await scope.ServiceProvider.GetRequiredService<ILastFmImportService>().RunAsync(job, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Recorded as Failed by the service; keep the worker alive
                    logger.LogDebug(ex, "Last.fm import {ImportId} ended with an error", job.ImportId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutdown
        }
    }
}
