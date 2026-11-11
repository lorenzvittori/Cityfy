using System.ComponentModel.DataAnnotations;

namespace CityFy.Api.Options;

/// <summary>"Frontend" section.</summary>
public class FrontendOptions
{
    public const string SectionName = "Frontend";

    /// <summary>Where the browser is sent back after the Spotify login (Angular dev server by default).</summary>
    [Required, Url]
    public string BaseUrl { get; set; } = "http://localhost:4200";

    /// <summary>Frontend page that shows login errors (?error=CODE).</summary>
    [Required]
    public string LoginPath { get; set; } = "/login";
}
