using ServiceDefault.City;

namespace CityFy.Api.Models;

// Public contract of the API (v1). Kept separate from the internal documents (CityModel, ...)
// so that internal changes do not break the frontend. Errors: ProblemDetails with "code" (ERRORI_UI.md).

/// <summary>Values of <see cref="MeDto.Kind"/> and <see cref="SessionDto.Kind"/>.</summary>
public static class SessionKinds
{
    /// <summary>No account: the data is deleted at ExpiresAt unless the user comes back before.</summary>
    public const string Anonymous = "anonymous";
    /// <summary>CityFy account, logged in with Last.fm: the data stays until the user deletes it.</summary>
    public const string LastFm = "lastfm";
}

/// <summary>
/// Who is using the app. Anonymous: DisplayName/LinkedAt/LastSyncAt null, ExpiresAt set (moves forward
/// with every visit). Last.fm: DisplayName = Last.fm user name, LastSyncAt = last reading, LastFm set.
/// </summary>
public sealed record MeDto(
    string Kind,
    string UserId,
    string? DisplayName,
    DateTime? LinkedAt,
    DateTime? LastSyncAt,
    DateTime? ExpiresAt,
    LastFmStatusDto? LastFm = null);

/// <summary>
/// Reading of the Last.fm account. Status: Active | Suspended (ErrorCode LASTFM_AUTH_FAILED or
/// LASTFM_USER_PRIVATE: log in again). LastScrobbleAt: newest play received. HistoryImportId: the
/// import of the history (GET api/v1/lastfm/imports/{id}). Warning: a hint, not an error
/// (LASTFM_SPOTIFY_NOT_SCROBBLING: no new play for days, check the Spotify connection on Last.fm).
/// </summary>
public sealed record LastFmStatusDto(
    string Username,
    string ProfileUrl,
    string Status,
    string? ErrorCode,
    DateTime LastScrobbleAt,
    DateTime? LastPolledAt,
    string? HistoryImportId,
    string? Warning = null);

/// <summary>
/// Event "now-playing" of api/v1/live. Playing false: nothing is playing (the other fields null).
/// ArtistMbid matches the ArtistId of the buildings; TrackUrl: page of the track on Last.fm (attribution).
/// </summary>
public sealed record NowPlayingDto(
    bool Playing,
    string? Artist,
    string? Track,
    string? Album,
    string? ArtistMbid,
    DateTime? StartedAt,
    string? TrackUrl)
{
    public static readonly NowPlayingDto Nothing = new(false, null, null, null, null, null, null);
}

/// <summary>Import of the Last.fm history: Pending, InProgress (progress), Done, Failed, Cancelled (+ errorCode).</summary>
public sealed record LastFmImportDto(
    string Id,
    string Status,
    int NewPlays,
    double? ProgressPercent,
    string? ProgressDetail,
    DateTime StartedAt,
    DateTime? CompletedAt,
    string? ErrorCode);

/// <summary>Session started (or already open). ExpiresAt: only for anonymous sessions.</summary>
public sealed record SessionDto(string Kind, DateTime? ExpiresAt);

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

/// <summary>Status of an upload: Pending, InProgress, Done, Failed, Cancelled (+ errorCode).</summary>
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
