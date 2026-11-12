using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using ServiceDefault.City;
using ServiceDefault.Models;

namespace CityFy.CityGenerator.Models;

/// <summary>
/// A city generated for a user ('generated_cities'): the request, the job status and, once Done,
/// the city. A snapshot: it does not change when the user's catalog changes.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class GeneratedCity
{
    [BsonId]
    public string CityId { get; set; } = null!;

    public string UserId { get; set; } = null!;

    /// <summary>Chosen by the user; null = the UI composes a title from the parameters.</summary>
    public string? Name { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? From { get; set; }

    /// <summary>Exclusive.</summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? To { get; set; }

    public string Layout { get; set; } = null!;

    /// <summary>Canonical parameter string of a "custom" layout.</summary>
    public string? Recipe { get; set; }

    public List<int> Genres { get; set; } = new();
    public List<int> Subgenres { get; set; } = new();

    /// <summary>Hash of the canonical parameters: an identical request on the same catalog reuses this city.</summary>
    public string ParamsHash { get; set; } = null!;

    /// <summary>Catalog version when requested, then the one the city was actually computed with.</summary>
    public string? CatalogVersion { get; set; }

    [BsonRepresentation(BsonType.String)]
    public ProcessingStatus Status { get; set; } = ProcessingStatus.Pending;

    /// <summary>Stable ErrorCodes value when Failed or Cancelled; Message is the technical detail.</summary>
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAt { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? CompletedAt { get; set; }

    public int Districts { get; set; }
    public int Artists { get; set; }
    public List<string> TopGenres { get; set; } = new();

    /// <summary>Districts > blocks > buildings; null until Done.</summary>
    public CityModel? City { get; set; }

    /// <summary>The map (positions, roads, street furniture), computed once; null until Done.</summary>
    public CityLayout? Map { get; set; }
}
