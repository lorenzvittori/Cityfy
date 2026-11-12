using CityFy.Retrieve.Spotify.Models;
using ServiceDefault.Listening;

namespace CityFy.Retrieve.Spotify.Mappers;

public sealed class SpotifyConverter : ISpotifyConverter
{
    public IReadOnlyList<ListeningEvent> ToListeningEvents(IEnumerable<SpotifyPlayHistoryDto> plays, string userId, string jobId)
    {
        return plays
            .Where(p => p.Track?.Uri?.StartsWith("spotify:track:", StringComparison.Ordinal) == true)
            .Select(p =>
            {
                var track = p.Track!;
                var artist = track.Artists?.FirstOrDefault();
                return new ListeningEvent
                {
                    UserId = userId,
                    PlayedAt = DateTime.SpecifyKind(p.PlayedAt.ToUniversalTime(), DateTimeKind.Utc),
                    TrackUri = track.Uri!,
                    TrackName = track.Name,
                    ArtistName = artist?.Name,
                    ArtistUri = artist?.Uri,
                    AlbumName = track.Album?.Name,
                    ContextUri = p.Context?.Uri,
                    SourceRef = jobId
                };
            })
            .ToList();
    }
}
