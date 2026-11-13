using CityFy.Retrieve.LastFm.Options;
using Microsoft.Extensions.Options;

namespace CityFy.Retrieve.LastFm.Clients;

/// <summary>Singleton: spaces the requests 1/MaxRequestsPerSecond apart and holds them all during a pause.</summary>
public sealed class LastFmRequestGate : ILastFmRequestGate, IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly TimeSpan _spacing;
    private readonly TimeSpan _minPause;
    private readonly TimeSpan _maxPause;
    private readonly object _pauseLock = new();

    private DateTime _nextSlot = DateTime.MinValue;
    private DateTime? _pausedUntil;
    private TimeSpan _nextPause;

    public LastFmRequestGate(IOptions<LastFmOptions> options)
    {
        var o = options.Value;
        _spacing = TimeSpan.FromSeconds(1 / o.MaxRequestsPerSecond);
        _minPause = TimeSpan.FromSeconds(o.RateLimitPauseSeconds);
        _maxPause = TimeSpan.FromSeconds(Math.Max(o.RateLimitPauseSeconds, o.MaxRateLimitPauseSeconds));
        _nextPause = _minPause;
    }

    public DateTime? PausedUntil
    {
        get
        {
            lock (_pauseLock)
                return _pausedUntil is { } until && until > DateTime.UtcNow ? until : null;
        }
    }

    /// <summary>Length of the next pause (doubles after every 29).</summary>
    internal TimeSpan NextPause
    {
        get { lock (_pauseLock) return _nextPause; }
    }

    public async Task WaitAsync(CancellationToken ct)
    {
        // One caller at a time: each one reserves its slot, the next one waits behind it
        await _lock.WaitAsync(ct);
        try
        {
            var now = DateTime.UtcNow;
            var start = _nextSlot > now ? _nextSlot : now;
            if (PausedUntil is { } until && until > start)
                start = until;

            if (start > now)
                await Task.Delay(start - now, ct);
            _nextSlot = start + _spacing;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Pause()
    {
        lock (_pauseLock)
        {
            var until = DateTime.UtcNow + _nextPause;
            if (_pausedUntil is null || until > _pausedUntil)
                _pausedUntil = until;
            _nextPause = _nextPause * 2 < _maxPause ? _nextPause * 2 : _maxPause;
        }
    }

    public void Succeeded()
    {
        lock (_pauseLock)
            _nextPause = _minPause;
    }

    public void Dispose() => _lock.Dispose();
}
