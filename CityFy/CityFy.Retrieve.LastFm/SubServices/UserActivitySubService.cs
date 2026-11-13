using System.Collections.Concurrent;
using CityFy.Retrieve.LastFm.Options;
using Microsoft.Extensions.Options;

namespace CityFy.Retrieve.LastFm.SubServices;

public sealed class UserActivitySubService(IOptions<LastFmOptions> options) : IUserActivitySubService
{
    private readonly TimeSpan _window = TimeSpan.FromSeconds(options.Value.ActiveWindowSeconds);
    private readonly ConcurrentDictionary<string, DateTime> _lastSeen = new();

    public void MarkActive(string userId, DateTime now) => _lastSeen[userId] = now;

    public bool IsActive(string userId, DateTime now) => _lastSeen.TryGetValue(userId, out var seen) && now - seen <= _window;

    public void Forget(string userId) => _lastSeen.TryRemove(userId, out _);

    public void ForgetAll() => _lastSeen.Clear();
}
