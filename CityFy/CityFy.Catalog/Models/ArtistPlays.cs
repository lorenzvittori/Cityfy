using MongoDB.Bson.Serialization.Attributes;

namespace CityFy.Catalog.Models;

/// <summary>Plays of one artist in the period, aggregated from 'listening_events'.</summary>
[BsonIgnoreExtraElements]
public sealed class ArtistPlays
{
    [BsonId]
    public string ArtistName { get; set; } = null!;

    /// <summary>spotify:artist:{id} when a play has it (none of the current sources writes it).</summary>
    public string? ArtistUri { get; set; }

    /// <summary>MusicBrainz artist id when at least one play came from a source that gives it (Last.fm).</summary>
    public string? ArtistMbid { get; set; }

    public long Plays { get; set; }

    /// <summary>Plays of at least CityOptions.QualifiedPlayMs (Last.fm scrobbles, without duration, count).</summary>
    public long QualifiedPlays { get; set; }

    public long MsPlayed { get; set; }
}
