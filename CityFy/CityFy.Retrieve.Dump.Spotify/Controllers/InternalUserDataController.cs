using CityFy.Retrieve.Dump.Spotify.Services;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.Models;

namespace CityFy.Retrieve.Dump.Spotify.Controllers;

/// <summary>Service-to-service endpoint used by CityFy.Api when a user (or the admin) deletes the user's data.</summary>
[ApiController]
[ApiExplorerSettings(GroupName = "Internal")]
[Route("internal/users")]
public sealed class InternalUserDataController(IUserDataService userData) : ControllerBase
{
    // DELETE internal/users/{userId}/data — stop imports, delete archives, files and upload tasks
    [HttpDelete("{userId}/data")]
    public async Task<ActionResult<UserDataDeletion>> Delete(string userId, CancellationToken ct) =>
        Ok(await userData.DeleteAsync(userId, ct));
}
