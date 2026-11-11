using System.ComponentModel.DataAnnotations;

namespace CityFy.Elaboration.Options;

/// <summary>"City" configuration section: knobs of the city builder.</summary>
public class CityOptions
{
    public const string SectionName = "City";

    /// <summary>A play counts towards the city from this duration (Spotify counts a stream at 30s).</summary>
    [Range(0, 600_000)]
    public int QualifiedPlayMs { get; set; } = 30_000;

    [Range(1, 1000)]
    public double MinHeight { get; set; } = 4;

    [Range(1, 1000)]
    public double MaxHeight { get; set; } = 40;

    [Range(1, 1000)]
    public double MinWidth { get; set; } = 6;

    [Range(1, 1000)]
    public double MaxWidth { get; set; } = 12;

    /// <summary>Below this share (0-100) of placed qualified plays the city gets the CITY_LOW_COVERAGE warning.</summary>
    [Range(0, 100)]
    public double LowCoveragePercent { get; set; } = 50;

    /// <summary>How many unresolved artists to keep in the model for diagnostics.</summary>
    [Range(0, 1000)]
    public int MaxUnresolvedReported { get; set; } = 50;

    /// <summary>Optional fixed colors per macro-genre name (e.g. "rock": "#80cb4d"); others get a hashed hue.</summary>
    public Dictionary<string, string> Palette { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Base address of Retrieve.MusicBrainz ("Services:MusicBrainz").</summary>
public class MusicBrainzServiceOptions
{
    public const string SectionName = "Services:MusicBrainz";

    /// <summary>Default resolved by Aspire service discovery (resource "RetrieveMusicBrainz").</summary>
    [Required]
    public string BaseUrl { get; set; } = "https+http://RetrieveMusicBrainz";
}
