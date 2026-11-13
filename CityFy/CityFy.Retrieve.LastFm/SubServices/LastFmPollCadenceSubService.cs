using CityFy.Retrieve.LastFm.Options;
using Microsoft.Extensions.Options;

namespace CityFy.Retrieve.LastFm.SubServices;

public sealed class LastFmPollCadenceSubService(IOptions<LastFmOptions> options) : ILastFmPollCadenceSubService
{
    private readonly LastFmOptions _o = options.Value;

    public DateTime NextPollAt(DateTime now, bool fast, DateTime? lastListeningAt)
    {
        if (fast)
            return now.AddSeconds(_o.ListeningIntervalSeconds);
        if (lastListeningAt is { } last && now - last <= TimeSpan.FromMinutes(_o.RecentWindowMinutes))
            return now.AddSeconds(_o.RecentIntervalSeconds);
        return now.AddSeconds(_o.IdleIntervalSeconds);
    }
}
