namespace CityFy.Retrieve.MusicBrainz.Clients;

/// <summary>Extracts .tar.bz2 dump archives with the system 'tar'.</summary>
public interface IArchiveExtractor
{
    /// <summary>Extracts <paramref name="members"/> (all when null) into <paramref name="destinationDirectory"/>.</summary>
    Task ExtractAsync(string archivePath, string destinationDirectory, IReadOnlyCollection<string>? members = null, CancellationToken cancellationToken = default);
}