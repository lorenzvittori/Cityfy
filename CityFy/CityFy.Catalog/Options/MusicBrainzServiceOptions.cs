using System.ComponentModel.DataAnnotations;

namespace CityFy.Catalog.Options;

/// <summary>Base address of Retrieve.MusicBrainz ("Services:MusicBrainz").</summary>
public sealed class MusicBrainzServiceOptions
{
    public const string SectionName = "Services:MusicBrainz";

    /// <summary>Default resolved by Aspire service discovery (resource "RetrieveMusicBrainz").</summary>
    [Required]
    public string BaseUrl { get; set; } = "https+http://RetrieveMusicBrainz";
}
