namespace CityFy.Retrieve.Dump.Spotify.Models;

public sealed record UploadCancelledResponse(string UploadId, string Status);

public sealed record LegacyMigrationResponse(string UploadId, string UserId, int Inserted);
