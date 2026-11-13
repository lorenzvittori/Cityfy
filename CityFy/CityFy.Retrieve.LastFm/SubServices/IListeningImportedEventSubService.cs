namespace CityFy.Retrieve.LastFm.SubServices;

public interface IListeningImportedEventSubService
{
    /// <summary>ListeningImported (source lastfm): the orchestrator refreshes the user's artist catalog.</summary>
    Task PublishAsync(string userId, string sourceRef, int newPlays, CancellationToken ct);
}
