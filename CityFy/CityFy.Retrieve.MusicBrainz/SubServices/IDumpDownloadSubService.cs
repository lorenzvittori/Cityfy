namespace CityFy.Retrieve.MusicBrainz.SubServices;

public interface IDumpDownloadSubService
{
    /// <summary>
    /// Ensures the archives of the latest complete export are available locally.
    /// Returns the local directory of the export (one sub-directory per export, named like the
    /// remote one: yyyyMMdd-HHmmss).
    /// </summary>
    Task<string> GetOrDownloadLatestAsync(IReadOnlyCollection<string> archives, CancellationToken cancellationToken = default);
}
