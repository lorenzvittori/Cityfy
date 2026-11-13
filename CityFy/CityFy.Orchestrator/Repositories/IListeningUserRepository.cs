namespace CityFy.Orchestrator.Repositories;

/// <summary>Read-only view of 'listening_events' (written by the retrieve services).</summary>
public interface IListeningUserRepository
{
    /// <summary>Every user with at least one play, i.e. every user with a city to rebuild.</summary>
    Task<IReadOnlyList<string>> GetUserIdsWithPlaysAsync(CancellationToken ct);
}
