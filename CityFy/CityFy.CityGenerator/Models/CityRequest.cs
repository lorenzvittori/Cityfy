namespace CityFy.CityGenerator.Models;

/// <summary>A validated generation request: UTC dates, known layout, genre ids sorted and distinct.</summary>
public sealed record CityRequest(
    string? Name,
    DateTime? From,
    DateTime? To,
    string Layout,
    string? Recipe,
    IReadOnlyList<int> Genres,
    IReadOnlyList<int> Subgenres)
{
    public const string Organic = "organic";
    public const string Grid = "grid";
    public const string Custom = "custom";

    /// <summary>Whole history, organic map, every genre: the first city of a new user.</summary>
    public static readonly CityRequest Default = new(null, null, null, Organic, null, [], []);
}
