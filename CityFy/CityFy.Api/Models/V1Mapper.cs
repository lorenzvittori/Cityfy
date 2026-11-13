using CityFy.Api.Clients;
using ServiceDefault.City;

namespace CityFy.Api.Models;

/// <summary>Internal documents -> public v1 DTOs.</summary>
public static class V1Mapper
{
    public static CityDto ToDto(this CityModel c) => new(
        c.GeneratedAt,
        c.From,
        c.To,
        new CitySummaryDto(c.Summary.TotalPlays, c.Summary.QualifiedPlays, c.Summary.ListeningHours,
            c.Summary.TotalArtists, c.Summary.PlacedArtists, c.Summary.CoveragePercent, c.Summary.TopGenres),
        c.Warnings,
        c.Districts.Select(ToDto).ToList());

    public static DistrictDto ToDto(this CityDistrict d) => new(
        d.DistrictId, d.Name, d.Color, d.Density, d.ListeningIntensity, d.Score,
        d.Blocks.Select(ToDto).ToList());

    public static BlockDto ToDto(this CityBlock b) => new(b.BlockId, b.GenreId, b.Name, b.Score, b.Buildings.Select(ToDto).ToList());

    public static BuildingDto ToDto(this CityBuilding b) => new(
        b.BuildingId, b.Name, b.SpotifyArtistUri, b.Plays, b.QualifiedPlays, b.ListeningMinutes,
        b.Height, b.Width, b.Color, b.Genres);

    public static GenerateCityRequest ToInternal(this CreateCityRequest r) =>
        new(r.Name, r.From, r.To, r.Layout, r.Recipe, r.Genres, r.Subgenres, r.MaxDistricts, r.ExtraGenres);

    public static GeneratedCityDto ToDto(this GeneratedCityInfo c) => new(
        c.CityId, c.Name, c.From, c.To, c.Layout, c.Recipe, c.Genres, c.Subgenres, c.Status, c.ErrorCode,
        c.CreatedAt, c.CompletedAt, c.Districts, c.Artists, c.TopGenres, c.MaxDistricts, c.ExtraGenres);

    public static CityFormOptionsDto ToDto(this CityFormOptions o) =>
        new(o.DefaultDistricts, o.WarnDistricts, o.MaxNameLength, o.MaxGenres);

    public static GeneratedCityDetailDto ToDto(this GeneratedCityDetail d) => new(d.Info.ToDto(), d.City?.ToDto(), d.Map);

    public static GeneratedCityPageDto ToDto(this GeneratedCityPage p) =>
        new(p.Items.Select(i => i.ToDto()).ToList(), p.Page, p.PageSize, p.Total);

    public static GenreOptionDto ToDto(this GenreOption g) => new(
        g.GenreId, g.Name, g.Color, g.QualifiedPlays, g.Artists,
        g.Subgenres.Select(ToDto).ToList());

    public static SubgenreOptionDto ToDto(this SubgenreOption s) => new(s.GenreId, s.Name, s.QualifiedPlays, s.Artists);

    public static ImportStatusDto ToDto(this TaskInfo t) =>
        new(t.UploadId, t.Status, t.Inserted, t.StartedAt, t.CompletedAt, t.ErrorCode);

    /// <summary>Attribution (Last.fm terms 2.7): the profile page of the user, linked from the UI.</summary>
    public static LastFmStatusDto ToDto(this LastFmAccountInfo a) => new(
        a.Username, $"https://www.last.fm/user/{Uri.EscapeDataString(a.Username)}", a.Status, a.ErrorCode,
        a.LastScrobbleAt, a.LastPolledAt, a.HistoryImportId, a.Warning);

    public static LastFmImportDto ToDto(this LastFmImportInfo i) =>
        new(i.ImportId, i.Status, i.Inserted, i.ProgressPercent, i.ProgressDetail, i.StartedAt, i.CompletedAt, i.ErrorCode);

    public static NowPlayingDto ToDto(this LastFmNowPlayingInfo? p) =>
        p is null ? NowPlayingDto.Nothing : NowPlaying(p.Artist, p.Track, p.Album, p.ArtistMbid, p.SeenAt);

    public static NowPlayingDto ToDto(this ServiceDefault.Contracts.NowPlayingChanged m) =>
        m.Artist is null || m.Track is null ? NowPlayingDto.Nothing : NowPlaying(m.Artist, m.Track, m.Album, m.ArtistMbid, m.StartedAt);

    private static NowPlayingDto NowPlaying(string artist, string track, string? album, string? mbid, DateTime? startedAt) =>
        new(true, artist, track, album, mbid, startedAt,
            $"https://www.last.fm/music/{Uri.EscapeDataString(artist)}/_/{Uri.EscapeDataString(track)}");

    public static PipelineRunDto ToDto(this PipelineRunInfo r) => new(
        r.RunId, r.Status, r.Triggers.Sum(t => t.NewPlays), r.Triggers.Select(t => t.Source).Distinct().ToList(),
        r.CreatedAt, r.StartedAt, r.CompletedAt, r.ErrorCode);
}
