using CityFy.CityGenerator.Services;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.City;

namespace CityFy.CityGenerator.Controllers;

[ApiController]
[ApiExplorerSettings(GroupName = "CityGenerator")]
[Route("api/city-generator/options")]
public sealed class FormOptionsController(ICityGenerationService cities) : ControllerBase
{
    // GET api/city-generator/options — defaults and limits of the city form (districts, name, genres)
    [HttpGet]
    public ActionResult<CityFormOptions> Get() => Ok(cities.GetFormOptions());
}
