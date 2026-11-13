namespace CityFy.Retrieve.MusicBrainz.SubServices;

public interface IDumpExtractSubService
{
    /// <summary>
    /// Extracts from the downloaded archives only the tables still missing (re-runs and partially
    /// extracted dumps), reporting progress. Returns the folder of the extracted tables ('mbdump').
    /// </summary>
    Task<string> ExtractMissingAsync(string localDir, CancellationToken ct);
}