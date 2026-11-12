using System.ComponentModel.DataAnnotations;

namespace CityFy.CityGenerator.Options;

/// <summary>"CityGenerator" configuration section: limits of the generated cities.</summary>
public sealed class CityGeneratorOptions
{
    public const string SectionName = "CityGenerator";

    /// <summary>Saved cities per user (any status): beyond it CITY_LIMIT_REACHED until one is deleted.</summary>
    [Range(1, 1000)]
    public int MaxCitiesPerUser { get; set; } = 10;

    [Range(1, 500)]
    public int MaxNameLength { get; set; } = 80;

    /// <summary>Genres + subgenres of one request.</summary>
    [Range(1, 10_000)]
    public int MaxGenres { get; set; } = 500;
}
