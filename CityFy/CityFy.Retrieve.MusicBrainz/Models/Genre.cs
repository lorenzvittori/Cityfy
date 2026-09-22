namespace CityFy.Retrieve.MusicBrainz.Models;

public class Genre
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    // MusicBrainz optional identifier (e.g., GUID)
    public string? MusicBrainzId { get; set; }

    public ICollection<GenreRelation> ParentRelations { get; set; } = new List<GenreRelation>();
    public ICollection<GenreRelation> ChildRelations { get; set; } = new List<GenreRelation>();
}
