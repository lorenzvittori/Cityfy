using CityFy.Retrieve.Spotify.Models;

namespace CityFy.Retrieve.Spotify.SubServices;

public interface IListeningImportedEventSubService
{
    /// <summary>Publishes ListeningImported (the orchestrator rebuilds the user's city).</summary>
    Task PublishAsync(RetrieveJob job, int newPlays, CancellationToken ct);
}