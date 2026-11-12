using CityFy.CityGenerator.Models.Layout;
using ServiceDefault.City;

namespace CityFy.CityGenerator.SubServices;

/// <summary>Pure: the map of a city (districts, blocks, lots, roads, street furniture). Deterministic.</summary>
public interface ICityLayoutSubService
{
    /// <summary>Every artist is placed exactly once (fallback: grid around the block center).</summary>
    CityLayout Compute(CityModel city, LayoutRecipe recipe);
}
