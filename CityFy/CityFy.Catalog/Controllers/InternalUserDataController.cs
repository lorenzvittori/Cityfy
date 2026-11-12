using CityFy.Catalog.Services;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.Models;

namespace CityFy.Catalog.Controllers;

/// <summary>Service-to-service endpoint used by CityFy.Api when a user (or the admin) deletes the user's data.</summary>
[ApiController]
[ApiExplorerSettings(GroupName = "Internal")]
[Route("internal/users")]
public sealed class InternalUserDataController(IUserDataService userData) : ControllerBase
{
    // DELETE internal/users/{userId}/data — every play (all sources) and every city of the user
    [HttpDelete("{userId}/data")]
    public async Task<ActionResult<UserDataDeletion>> Delete(string userId, CancellationToken ct) =>
        Ok(await userData.DeleteAsync(userId, ct));
}
