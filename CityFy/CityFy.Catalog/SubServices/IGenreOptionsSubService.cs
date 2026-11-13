using CityFy.Catalog.Models;
using ServiceDefault.City;

namespace CityFy.Catalog.SubServices;

/// <summary>Pure: genres and subgenres a city can be filtered on, from the plays of a range.</summary>
public interface IGenreOptionsSubService
{
    /// <summary>Districts (top-level genres) with their subgenres, most listened first; only placed artists with qualified plays count.</summary>
    IReadOnlyList<GenreOption> List(IReadOnlyList<ArtistPlays> plays, IReadOnlyDictionary<string, ArtistPlacement> placementsByArtistName);
}
