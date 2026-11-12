namespace CityFy.Retrieve.Dump.Spotify.SubServices;

public interface ILegacyUploadMigrationSubService
{
    /// <summary>
    /// Copies a legacy per-upload collection (pre 'listening_events') into 'listening_events' for
    /// the given user. Idempotent. Returns the number of new plays.
    /// </summary>
    Task<int> MigrateAsync(string uploadId, string userId, CancellationToken ct);
}