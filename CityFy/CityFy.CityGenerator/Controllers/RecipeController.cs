using CityFy.CityGenerator.Services;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.City;

namespace CityFy.CityGenerator.Controllers;

[ApiController]
[ApiExplorerSettings(GroupName = "CityGenerator")]
[Route("api/city-generator/recipe")]
public sealed class RecipeController(ICityGenerationService cities) : ControllerBase
{
    // GET api/city-generator/recipe/schema — keys, ranges and presets of the parameter string of a custom map
    [HttpGet("schema")]
    public ActionResult<RecipeSchema> Schema() => Ok(cities.GetRecipeSchema());
}
