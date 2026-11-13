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

    // POST internal/users/{userId}/transfer { toUserId } — the finished uploads change owner
    // (complete: false while an import is still running)
    [HttpPost("{userId}/transfer")]
    public async Task<ActionResult<UserDataTransfer>> Transfer(string userId, [FromBody] UserDataTransferRequest request, CancellationToken ct) =>
        Ok(await userData.TransferAsync(userId, request.ToUserId, ct));
}
