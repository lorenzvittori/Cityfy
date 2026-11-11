using System.Threading.Channels;
using CityFy.Retrieve.MusicBrainz.Models;

namespace CityFy.Retrieve.MusicBrainz.Services;

public enum RetrieveState
{
    Idle,
    Running,
    Done,
    Failed
}

public sealed record RetrieveStatus(
    RetrieveState State,
    DateTime? StartedAt = null,
    DateTime? CompletedAt = null,
    RetrieveResult? Result = null,
    string? Error = null);

/// <summary>
/// Runs the MusicBrainz retrieve in the background, one at a time (it takes minutes to hours
/// and replaces the whole database). Status is kept in memory.
/// </summary>
public class RetrieveRunner : BackgroundService
{
    private readonly Channel<bool> _trigger = Channel.CreateBounded<bool>(1);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RetrieveRunner> _logger;
    private volatile RetrieveStatus _status = new(RetrieveState.Idle);

    public RetrieveRunner(IServiceScopeFactory scopeFactory, ILogger<RetrieveRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public RetrieveStatus Status => _status;

    /// <summary>False if a retrieve is already queued or running.</summary>
    public bool TryStart()
    {
        if (_status.State == RetrieveState.Running) return false;
        return _trigger.Writer.TryWrite(true);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var _ in _trigger.Reader.ReadAllAsync(stoppingToken))
            {
                var startedAt = DateTime.UtcNow;
                _status = new RetrieveStatus(RetrieveState.Running, startedAt);

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<RetrieveService>();
                    var result = await service.RunRetrieveAsync(stoppingToken);
                    _status = new RetrieveStatus(RetrieveState.Done, startedAt, DateTime.UtcNow, result);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "MusicBrainz retrieve failed");
                    _status = new RetrieveStatus(RetrieveState.Failed, startedAt, DateTime.UtcNow, Error: ex.Message);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutdown
        }
    }
}
