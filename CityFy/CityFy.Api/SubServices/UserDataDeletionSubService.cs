using CityFy.Api.Clients;
using ServiceDefault.Errors;
using ServiceDefault.Models;

namespace CityFy.Api.SubServices;

public sealed class UserDataDeletionSubService(
    ISpotifyServiceClient spotify,
    IDumpClient dump,
    IOrchestratorClient orchestrator,
    IElaborationClient elaboration,
    ILogger<UserDataDeletionSubService> logger) : IUserDataDeletionSubService
{
    public async Task<(IReadOnlyList<UserDataDeletion> Deleted, IReadOnlyList<string> Failed)> DeleteEverywhereAsync(string userId, CancellationToken ct)
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
        return (deleted, failed);
    }
}
