using CityFy.CityGenerator.Models.Layout;
using ServiceDefault.City;

namespace CityFy.CityGenerator.SubServices;

/// <summary>Pure: map parameters from the layout name and, for "custom", the parameter string.</summary>
public interface ILayoutRecipeParseSubService
{
    /// <summary>
    /// "organic" / "grid": the preset; "custom": the parameter string over its base. Throws
    /// CITY_INVALID_RECIPE (400) with "problems" (key + RecipeProblemCodes) for an invalid string.
    /// </summary>
    LayoutRecipe Resolve(string layout, string? recipe);

    /// <summary>Every key in a fixed order, values normalized: equal parameters give equal strings (hash, storage).</summary>
    string Canonical(LayoutRecipe recipe);

    RecipeSchema Schema();
}
