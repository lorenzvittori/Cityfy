using CityFy.Orchestrator.Options;
using CityFy.Orchestrator.Repositories;
using Microsoft.Extensions.Options;
using ServiceDefault.Contracts;
using ServiceDefault.Messaging;

namespace CityFy.Orchestrator.HostedServices;

/// <summary>
/// Starts the Pending runs whose debounce is over (publishes BuildCityRequested) and fails the
/// Building runs that got no answer within the timeout.
/// </summary>
public sealed class PipelineScheduler : BackgroundService
{
    private readonly PipelineRunRepository _repo;
    private readonly IMessagePublisher _publisher;
    private readonly PipelineOptions _options;
    private readonly ILogger<PipelineScheduler> _logger;

    public PipelineScheduler(PipelineRunRepository repo, IMessagePublisher publisher, IOptions<PipelineOptions> options, ILogger<PipelineScheduler> logger)
    {
        _repo = repo;
        _publisher = publisher;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollSeconds));
        do
        {
            try
            {
                await TickAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pipeline scheduler tick failed");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private async Task TickAsync(DateTime now, CancellationToken ct)
    {
        var stuck = await _repo.FailStuckAsync(now.AddMinutes(-_options.BuildTimeoutMinutes), now, ct);
        if (stuck > 0)
            _logger.LogWarning("{Count} pipeline runs timed out", stuck);

        foreach (var run in await _repo.ClaimDueRunsAsync(now, ct))
        {
            try
            {
                await _publisher.PublishAsync(new BuildCityRequested(run.RunId, run.UserId, run.Periods), correlationId: run.RunId, ct);
                _logger.LogInformation("Run {RunId}: city build requested for {UserId} ({Triggers} imports)", run.RunId, run.UserId, run.Triggers.Count);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "Run {RunId}: cannot publish BuildCityRequested, retrying later", run.RunId);
                await _repo.ReleaseAsync(run.RunId, now.AddSeconds(Math.Max(_options.DebounceSeconds, 10)), ex.Message, ct);
            }
        }
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
