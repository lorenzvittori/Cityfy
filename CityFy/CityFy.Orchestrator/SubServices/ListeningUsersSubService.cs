using CityFy.Orchestrator.Repositories;

namespace CityFy.Orchestrator.SubServices;

public sealed class ListeningUsersSubService(IListeningUserRepository repo) : IListeningUsersSubService
{
    public Task<IReadOnlyList<string>> GetUserIdsWithPlaysAsync(CancellationToken ct) => repo.GetUserIdsWithPlaysAsync(ct);
}
