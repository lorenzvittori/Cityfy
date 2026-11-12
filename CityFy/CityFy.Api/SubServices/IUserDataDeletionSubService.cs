using ServiceDefault.Models;

namespace CityFy.Api.SubServices;

public interface IUserDataDeletionSubService
{
    /// <summary>
    /// Asks every service that owns user data to delete it, in this order:
    ///   1. RetrieveSpotify: stops the retrieves, deletes jobs and the linked account;
    ///   2. RetrieveDumpSpotify: stops the imports, deletes archives, extracted files, tasks;
    ///   3. Orchestrator: deletes the pipeline runs (queued builds never start);
    ///   4. CityGenerator: stops the generations, deletes the saved cities;
    ///   5. Catalog: plays, catalog and period cities, last, when nothing can write new plays anymore.
    /// A service that fails does not stop the others: it is listed in Failed (every step is idempotent).
    /// </summary>
    Task<(IReadOnlyList<UserDataDeletion> Deleted, IReadOnlyList<string> Failed)> DeleteEverywhereAsync(string userId, CancellationToken ct);
}
