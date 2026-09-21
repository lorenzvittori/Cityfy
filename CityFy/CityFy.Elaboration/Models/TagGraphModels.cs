namespace CityFy.Elaboration.Models;

public class RelatedTag
{
    public string? Tag { get; set; }
    public int Count { get; set; }
}

public class TagGraph
{
    public string? SeedTag { get; set; }
    public DateTime RetrievedAtUtc { get; set; }
    public List<RelatedTag>? RelatedTags { get; set; }
}
