using MongoDB.Bson.Serialization.Attributes;

namespace CityFy.Elaboration.Models;

/// <summary>Plays of one artist in the period, aggregated from 'listening_events'.</summary>
[BsonIgnoreExtraElements]
public class ArtistPlays
{
    [BsonId]
    public string ArtistName { get; set; } = null!;

    /// <summary>spotify:artist:{id} when at least one play came from the API.</summary>
    public string? ArtistUri { get; set; }

    public long Plays { get; set; }

    /// <summary>Plays of at least CityOptions.QualifiedPlayMs (API plays, without duration, count).</summary>
    public long QualifiedPlays { get; set; }

    public long MsPlayed { get; set; }
}
