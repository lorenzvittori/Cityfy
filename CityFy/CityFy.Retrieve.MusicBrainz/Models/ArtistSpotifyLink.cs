namespace CityFy.Retrieve.MusicBrainz.Models;

/// <summary>
/// Spotify artist page linked to a MusicBrainz artist (l_artist_url + url,
/// https://open.spotify.com/artist/{id}). Reliable key to match Spotify plays.
/// </summary>
public sealed class ArtistSpotifyLink
{
    public int ArtistId { get; set; }
    public Artist Artist { get; set; } = null!;

    /// <summary>Base62 Spotify artist id (22 chars), without the "spotify:artist:" prefix.</summary>
    public string SpotifyArtistId { get; set; } = null!;
}
