using CityFy.Retrieve.MusicBrainz.Models;

namespace CityFy.Retrieve.MusicBrainz.Clients;

/// <summary>MusicBrainz full-export server (data.metabrainz.org).</summary>
public interface IMbDumpClient
{
    Task<RemoteFileInfo?> GetRemoteFileInfoAsync(string fileNameOrUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Most recent export directory (yyyyMMdd-HHmmss) that contains all the requested archives.
    /// Newer directories that are still incomplete (upload in progress) are skipped.
    /// </summary>
    Task<string?> FindLatestDumpDirectoryAsync(IReadOnlyCollection<string> requiredFiles, CancellationToken cancellationToken = default);

    Task DownloadAsync(string fileNameOrUrl, string destinationPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}