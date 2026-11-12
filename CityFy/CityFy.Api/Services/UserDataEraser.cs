using CityFy.Api.Clients;
using CityFy.Api.Models;
using ServiceDefault.Errors;
using ServiceDefault.Models;

namespace CityFy.Api.Services;

/// <summary>
/// Deletes everything Cityfy keeps about a user. The data lives in four services, each deletes its own,
/// in this order:
///   1. RetrieveSpotify: stops the user's retrieves, deletes jobs and the linked account (no more
///      scheduled retrieves, refresh token gone);
///   2. RetrieveDumpSpotify: stops the imports, deletes uploaded archives, extracted files, tasks;
///   3. Orchestrator: deletes the pipeline runs (queued builds never start);
///   4. Elaboration: deletes plays and cities, last, when nothing can write new plays anymore
///      (a build already running does not store a city for a user without plays).
/// Every step is idempotent: when one fails the others still run and the whole deletion can simply
/// be repeated (USER_DATA_DELETION_INCOMPLETE).
/// </summary>
public sealed class UserDataEraser(
    ISpotifyServiceClient spotify,
    IDumpClient dump,
    IOrchestratorClient orchestrator,
    IElaborationClient elaboration,
    ILogger<UserDataEraser> logger) : IUserDataEraser
{
    public async Task<UserDataDeletionResult> DeleteAsync(string userId, string requestedBy, CancellationToken ct)
    {
        var owners = new (string Name, IUserDataOwner Owner)[]
        {
            ("RetrieveSpotify", spotify),
            ("RetrieveDumpSpotify", dump),
            ("Orchestrator", orchestrator),
            ("Elaboration", elaboration),
        };

        var deleted = new List<UserDataDeletion>();
        var failed = new List<string>();
        foreach (var (name, owner) in owners)
        {
            try
            {
                deleted.Add(await owner.DeleteUserDataAsync(userId, ct));
            }
            catch (CityFyException ex)
            {
                logger.LogError("Data deletion of user {UserId}: {Service} failed ({Code}) {Detail}", userId, name, ex.Code, ex.Message);
                failed.Add(name);
            }
        }

        if (failed.Count > 0)
            throw new CityFyException(ErrorCodes.UserDataDeletionIncomplete,
                $"Data of user '{userId}' not completely deleted: {string.Join(", ", failed)} did not answer. Retry to complete.",
                StatusCodes.Status502BadGateway);

        logger.LogInformation("Data of user {UserId} deleted (requested by {RequestedBy}): {Summary}", userId, requestedBy,
            string.Join("; ", deleted.Select(d => $"{d.Service} {string.Join(", ", d.Deleted.Select(kv => $"{kv.Key}={kv.Value}"))}")));
        return new UserDataDeletionResult(userId, deleted);
    }
}
