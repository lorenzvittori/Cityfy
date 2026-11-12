using System.ComponentModel.DataAnnotations;

namespace CityFy.CityGenerator.Options;

/// <summary>Base address of CityFy.Catalog ("Services:Catalog").</summary>
public sealed class CatalogServiceOptions
{
    public const string SectionName = "Services:Catalog";

    /// <summary>Default resolved by Aspire service discovery (resource "Catalog").</summary>
    [Required]
    public string BaseUrl { get; set; } = "https+http://Catalog";
}
