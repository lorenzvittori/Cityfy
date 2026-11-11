using System.Threading.Channels;
using CityFy.Retrieve.MusicBrainz.Models;

namespace CityFy.Retrieve.MusicBrainz.Services;

public enum RetrieveState
{
    Idle,
    Running,
    Done,
    Failed,

    /// <summary>Stopped by the admin: the database was left as it was before (the write is transactional).</summary>
    Cancelled
}

/// <param name="Progress">Current step while Running; on Failed, the step where it stopped.</param>
public sealed record RetrieveStatus(
    RetrieveState State,
    DateTime? StartedAt = null,
    DateTime? CompletedAt = null,
    RetrieveResult? Result = null,
    string? Error = null,
    ImportProgressSnapshot? Progress = null);

/// <summary>
/// Runs the MusicBrainz retrieve in the background, one at a time (it takes minutes to hours
/// and replaces the whole database). Status and progress are kept in memory.
/// </summary>
public class RetrieveRunner : BackgroundService
{
    private readonly Channel<bool> _trigger = Channel.CreateBounded<bool>(1);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ImportProgress _progress;
    private readonly ILogger<RetrieveRunner> _logger;
    private volatile RetrieveStatus _status = new(RetrieveState.Idle);
    private volatile CancellationTokenSource? _current;

    public RetrieveRunner(IServiceScopeFactory scopeFactory, ImportProgress progress, ILogger<RetrieveRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _progress = progress;
        _logger = logger;
    }

    public RetrieveStatus Status
    {
        get
        {
            var status = _status;
            return status.State is RetrieveState.Running or RetrieveState.Failed or RetrieveState.Cancelled
                ? status with { Progress = _progress.Current }
                : status;
        }
    }

    /// <summary>Stops the running import (download, tar, reading or the write transaction). False if none is running.</summary>
    public bool Cancel()
    {
        var cts = _current;
        if (_status.State != RetrieveState.Running || cts == null) return false;
        try
        {
            cts.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false; // finished in the meantime
        }
    }

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
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                _current = cts;
                _progress.Report("Avvio", null, "Ricerca dell'ultimo export MusicBrainz");
                _status = new RetrieveStatus(RetrieveState.Running, startedAt);

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<RetrieveService>();
                    var result = await service.RunRetrieveAsync(cts.Token);
                    _status = new RetrieveStatus(RetrieveState.Done, startedAt, DateTime.UtcNow, result);
                    _progress.Clear();
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception) when (cts.IsCancellationRequested)
                {
                    // Cancelled by the admin (tar may surface it as a non-zero exit, not as OCE)
                    _logger.LogWarning("MusicBrainz retrieve cancelled by the admin");
                    _status = new RetrieveStatus(RetrieveState.Cancelled, startedAt, DateTime.UtcNow,
                        Error: "Annullato dall'amministratore: il catalogo è rimasto quello precedente");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "MusicBrainz retrieve failed");
                    _status = new RetrieveStatus(RetrieveState.Failed, startedAt, DateTime.UtcNow, Error: ex.Message);
                }
                finally
                {
                    _current = null;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutdown
        }
    }
}
