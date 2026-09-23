namespace CityFy.Retrieve.MusicBrainz.Models;

public class ArtistGenreRelation
{
    public int Id { get; set; }

    public int ArtistId { get; set; }
    public Artist Artist { get; set; } = null!;

    public int GenreId { get; set; }
    public Genre Genre { get; set; } = null!;

    public string? RelationType { get; set; }
}
