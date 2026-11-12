using ServiceDefault.City;

namespace CityFy.Api.Models;

// Public contract of the API (v1). Kept separate from the internal documents (CityModel, ...)
// so that internal changes do not break the frontend. Errors: ProblemDetails with "code" (ERRORI_UI.md).

public sealed record MeDto(string UserId, string? DisplayName, DateTime? LinkedAt, DateTime? LastSyncAt);

// --- City -------------------------------------------------------------------

/// <summary>Data of a generated city (districts > blocks > buildings); From/To: range, To exclusive.</summary>
public sealed record CityDto(
    DateTime GeneratedAt,
    DateTime? From,
    DateTime? To,
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

// --- Generated cities -----------------------------------------------------------

/// <summary>
/// A city to generate. Name optional (null = the UI composes a title). From/To: UTC, To exclusive.
/// Layout: "organic" (default) | "grid" | "custom" (Recipe: parameter string, see GET api/v1/cities/recipe/schema). Genres = district ids, Subgenres = block ids (GenreOptionDto).
/// </summary>
public sealed record CreateCityRequest(
    string? Name,
    DateTime? From,
    DateTime? To,
    string? Layout,
    string? Recipe,
    IReadOnlyList<int>? Genres,
    IReadOnlyList<int>? Subgenres);

/// <summary>Status: Pending, InProgress, Done, Failed, Cancelled (+ errorCode). TopGenres: main districts, for a default title.</summary>
public sealed record GeneratedCityDto(
    string CityId,
    string? Name,
    DateTime? From,
    DateTime? To,
    string Layout,
    string? Recipe,
    IReadOnlyList<int> Genres,
    IReadOnlyList<int> Subgenres,
    string Status,
    string? ErrorCode,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    int DistrictCount,
    int ArtistCount,
    IReadOnlyList<string> TopGenres);

/// <summary>
/// City (data) and Map (geometry, scene units): null until Status is Done. Map is the shared
/// ServiceDefault.City.CityLayout contract, exposed as-is (pure geometric output).
/// </summary>
public sealed record GeneratedCityDetailDto(GeneratedCityDto Info, CityDto? City, CityLayout? Map);

public sealed record GeneratedCityPageDto(IReadOnlyList<GeneratedCityDto> Items, int Page, int PageSize, long Total);

/// <summary>A district (top-level genre) that can be chosen, with its subgenres.</summary>
public sealed record GenreOptionDto(int GenreId, string Name, string Color, long QualifiedPlays, int ArtistCount, IReadOnlyList<SubgenreOptionDto> Subgenres);

public sealed record SubgenreOptionDto(int GenreId, string Name, long QualifiedPlays, int ArtistCount);

// --- Imports and pipeline -----------------------------------------------------

public sealed record UploadAcceptedDto(string UploadId);

public sealed record SyncAcceptedDto(string JobId);

/// <summary>Status of an upload or of a Spotify sync: Pending, InProgress, Done, Failed, Cancelled (+ errorCode).</summary>
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
