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

    // POST internal/users/{userId}/transfer { toUserId } — the plays change owner (anonymous session ->
    // account); the old owner's catalog is deleted, the new one is rebuilt by the pipeline
    [HttpPost("{userId}/transfer")]
    public async Task<ActionResult<UserDataTransfer>> Transfer(string userId, [FromBody] UserDataTransferRequest request, CancellationToken ct) =>
        Ok(await userData.TransferAsync(userId, request.ToUserId, ct));
}
