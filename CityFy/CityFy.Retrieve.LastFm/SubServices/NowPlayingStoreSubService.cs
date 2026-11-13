using System.Collections.Concurrent;
using CityFy.Retrieve.LastFm.Models;

namespace CityFy.Retrieve.LastFm.SubServices;

public sealed class NowPlayingStoreSubService : INowPlayingStoreSubService
{
    private readonly ConcurrentDictionary<string, NowPlaying> _playing = new();

    public bool Set(string userId, LastFmNowPlaying? playing, DateTime now)
    {
        var previous = _playing.TryGetValue(userId, out var p) ? p : null;
        if (playing is null)
            return _playing.TryRemove(userId, out _);

        var same = previous is not null && previous.Artist == playing.Artist && previous.Track == playing.Track;
        // Same track: keep when it was first seen
        _playing[userId] = new NowPlaying(playing.Artist, playing.Track, playing.Album, playing.ArtistMbid, same ? previous!.SeenAt : now);
        return !same;
    }

    public NowPlaying? Get(string userId) => _playing.TryGetValue(userId, out var p) ? p : null;

    public bool Forget(string userId) => _playing.TryRemove(userId, out _);

    public void ForgetAll() => _playing.Clear();
}
