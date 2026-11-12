using CityFy.Catalog.Models;
using CityFy.Catalog.Services;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.Errors;

namespace CityFy.Catalog.Controllers;

[ApiController]
[Route("debug/catalog")]
public sealed class DebugCatalogController(ICatalogRefreshService catalog) : ControllerBase
{
    // POST debug/catalog/refresh/{userId} — rebuilds the user's artist catalog synchronously, outside the
    // pipeline (no event), and returns its version and counts
    [HttpPost("refresh/{userId}")]
    public async Task<ActionResult<UserCatalog>> Refresh(string userId, CancellationToken ct) =>
        await catalog.RefreshAsync(userId, ct) is { } c
            ? Ok(c)
            : this.CityFyProblem(ErrorCodes.CityNoPlays, $"User '{userId}' has no plays", StatusCodes.Status404NotFound);
}
