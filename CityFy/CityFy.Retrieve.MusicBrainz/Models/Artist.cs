namespace CityFy.Retrieve.MusicBrainz.Models;

public class Artist
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? MusicBrainzId { get; set; }

    public ICollection<ArtistGenreRelation> GenreRelations { get; set; } = new List<ArtistGenreRelation>();
}
