namespace CityFy.Orchestrator.SubServices;

public interface IListeningUsersSubService
{
    /// <summary>Every user with at least one play, i.e. every user with a city to rebuild.</summary>
    Task<IReadOnlyList<string>> GetUserIdsWithPlaysAsync(CancellationToken ct);
}
