using CityFy.Retrieve.LastFm.Models;
using ServiceDefault.Listening;

namespace CityFy.Retrieve.LastFm.Mappers;

public interface ILastFmListeningMapper
{
    /// <summary>Scrobbles → plays of <paramref name="userId"/> ('listening_events', source lastfm).</summary>
    IReadOnlyList<ListeningEvent> ToListeningEvents(IEnumerable<LastFmScrobble> scrobbles, string userId, string sourceRef);
}
