namespace CityFy.Retrieve.MusicBrainz.Models;

/// <summary>
/// MusicBrainz artist. Only artists with at least one genre are imported: the others cannot be
/// placed in the city anyway. Id is the MusicBrainz row id (not generated).
/// </summary>
public sealed class Artist
{
    public int Id { get; set; }

    /// <summary>MusicBrainz artist MBID.</summary>
    public string Gid { get; set; } = null!;

    public string Name { get; set; } = null!;

    /// <summary>Sum of the genre tag votes: used to pick the best candidate among homonyms.</summary>
    public int TotalGenreVotes { get; set; }

    public ICollection<ArtistGenre> Genres { get; set; } = new List<ArtistGenre>();
    public ICollection<ArtistSpotifyLink> SpotifyLinks { get; set; } = new List<ArtistSpotifyLink>();
}
