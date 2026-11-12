namespace CityFy.Retrieve.MusicBrainz.Models;

/// <summary>
/// Genre of an artist, from the MusicBrainz tags (derived dump: artist_tag) whose name is an
/// official genre. Votes = tag count.
/// </summary>
public sealed class ArtistGenre
{
    public int ArtistId { get; set; }
    public Artist Artist { get; set; } = null!;

    public int GenreId { get; set; }
    public Genre Genre { get; set; } = null!;

    public int Votes { get; set; }
}
