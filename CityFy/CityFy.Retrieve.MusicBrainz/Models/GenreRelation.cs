namespace CityFy.Retrieve.MusicBrainz.Models;

public class GenreRelation
{
    public int Id { get; set; }

    public int ParentId { get; set; }
    public Genre Parent { get; set; } = null!;

    public int ChildId { get; set; }
    public Genre Child { get; set; } = null!;

    // relation type, e.g. "subgenre" or other descriptor
    public string? RelationType { get; set; }
}
