namespace CityFy.Retrieve.MusicBrainz.Models;

public class ArtistTags
{
    public string? ArtistId { get; set; }
    public List<string>? Tags { get; set; }
}

public class RelatedTag
{
    public string? Tag { get; set; }
    public int Count { get; set; }
}

public class TagGraph
{
    /// <summary>
    /// Tag seed usato per la ricerca (es. "rock").
    /// </summary>
    public string? SeedTag { get; set; }

    /// <summary>
    /// Timestamp UTC di quando è stata eseguita la raccolta.
    /// </summary>
    public DateTime RetrievedAtUtc { get; set; }

    /// <summary>
    /// Lista di tag correlati con conteggio.
    /// </summary>
    public List<RelatedTag>? RelatedTags { get; set; }
}
