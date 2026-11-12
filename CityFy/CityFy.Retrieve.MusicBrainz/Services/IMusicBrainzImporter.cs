using CityFy.Retrieve.MusicBrainz.Models;

namespace CityFy.Retrieve.MusicBrainz.Services;

/// <summary>
/// Reads the extracted dump tables by column position (MusicBrainz schema) and replaces the
/// content of the database in a single transaction.
/// </summary>
public interface IMusicBrainzImporter
{
    Task<RetrieveResult> ImportAsync(string mbdumpDir, CancellationToken ct);

    /// <summary>Reads the tables only (no database access).</summary>
    MusicBrainzData Read(string mbdumpDir, CancellationToken ct);
}