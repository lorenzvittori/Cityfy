using CityFy.Api.Clients;
using ServiceDefault.Errors;
using ServiceDefault.Models;

namespace CityFy.Api.SubServices;

public sealed class UserDataDeletionSubService(
    ILastFmServiceClient lastFm,
    IDumpClient dump,
    IOrchestratorClient orchestrator,
    ICityGeneratorClient cityGenerator,
    ICatalogClient catalog,
    ILogger<UserDataDeletionSubService> logger) : IUserDataDeletionSubService
{
    public async Task<(IReadOnlyList<UserDataDeletion> Deleted, IReadOnlyList<string> Failed)> DeleteEverywhereAsync(string userId, CancellationToken ct)
    {
        // Sources first (they stop their jobs, so nothing writes plays any more), Catalog (plays) last
        var owners = new (string Name, IUserDataOwner Owner)[]
        {
            ("RetrieveLastFm", lastFm),
            ("RetrieveDumpSpotify", dump),
            ("Orchestrator", orchestrator),
            ("CityGenerator", cityGenerator),
            ("Catalog", catalog),
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
