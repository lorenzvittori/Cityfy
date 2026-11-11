namespace CityFy.Api.Models;

// Public contract of the API (v1). Kept separate from the internal documents (CityModel, ...)
// so that internal changes do not break the frontend. Errors: ProblemDetails with "code" (ERRORI_UI.md).

public sealed record MeDto(string UserId, string? DisplayName, DateTime? LinkedAt, DateTime? LastSyncAt);

// --- City -------------------------------------------------------------------

public sealed record CityDto(
    string Period,
    DateTime GeneratedAt,
    DateTime? From,
    CitySummaryDto Summary,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<DistrictDto> Districts);

public sealed record CitySummaryDto(
    long TotalPlays,
    long QualifiedPlays,
    double ListeningHours,
    int TotalArtists,
    int PlacedArtists,
    double CoveragePercent,
    IReadOnlyList<string> TopGenres);

public sealed record DistrictDto(
    string DistrictId,
    string Name,
    string Color,
    double Density,
    double ListeningIntensity,
    long Score,
    IReadOnlyList<BlockDto> Blocks);

/// <summary>Block = genre. GenreId is the MusicBrainz genre id (stable across periods).</summary>
public sealed record BlockDto(string BlockId, int GenreId, string Name, long Score, IReadOnlyList<BuildingDto> Buildings);

/// <summary>Building = artist. ArtistId is the MusicBrainz artist MBID.</summary>
public sealed record BuildingDto(
    string ArtistId,
    string Name,
    string? SpotifyArtistUri,
    long Plays,
    long QualifiedPlays,
    double ListeningMinutes,
    double Height,
    double Width,
    string Color,
    IReadOnlyList<string> Genres);

/// <summary>District without its blocks (list views, legends).</summary>
public sealed record DistrictSummaryDto(
    string DistrictId,
    string Name,
    string Color,
    double Density,
    double ListeningIntensity,
    long Score,
    int GenreCount,
    int ArtistCount);

/// <summary>Genre aggregate (one block of the city) with its district.</summary>
public sealed record GenreDto(
    int GenreId,
    string Name,
    string DistrictId,
    string DistrictName,
    string Color,
    long Score,
    int ArtistCount);

public sealed record GenreDetailDto(GenreDto Genre, IReadOnlyList<BuildingDto> Artists);

public sealed record ArtistDetailDto(BuildingDto Artist, string DistrictId, string DistrictName, int GenreId, string GenreName);

// --- Imports and pipeline -----------------------------------------------------

/// <summary>Status of an upload or of a Spotify sync: Pending, WORKING_IN_PROGRESS, DONE, FAILED (+ errorCode).</summary>
public sealed record ImportStatusDto(string Id, string Status, int NewPlays, DateTime StartedAt, DateTime? CompletedAt, string? ErrorCode);

/// <summary>City build: Pending, Building, Done, Failed (+ errorCode).</summary>
public sealed record PipelineRunDto(
    string RunId,
    string Status,
    int NewPlays,
    IReadOnlyList<string> Sources,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? ErrorCode);
