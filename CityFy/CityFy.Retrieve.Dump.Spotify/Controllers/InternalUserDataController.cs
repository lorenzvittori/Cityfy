using CityFy.Retrieve.Dump.Spotify.Services;
using Microsoft.AspNetCore.Mvc;

namespace CityFy.Retrieve.Dump.Spotify.Controllers;

/// <summary>Service-to-service endpoint used by CityFy.Api when a user (or the admin) deletes the user's data.</summary>
[ApiController]
[ApiExplorerSettings(GroupName = "Internal")]
[Route("internal/users")]
public class InternalUserDataController : ControllerBase
{
    // DELETE internal/users/{userId}/data — stop imports, delete archives, files and upload tasks
    [HttpDelete("{userId}/data")]
    public async Task<IActionResult> Delete(string userId, [FromServices] UserDataService data, CancellationToken ct) =>
        Ok(await data.DeleteAsync(userId, ct));
}
