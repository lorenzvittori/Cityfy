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
        d.Blocks.Select(b => new BlockDto(b.BlockId, b.GenreId, b.Name, b.Score, b.Buildings.Select(ToDto).ToList())).ToList());

    public static BuildingDto ToDto(this CityBuilding b) => new(
        b.BuildingId, b.Name, b.SpotifyArtistUri, b.Plays, b.QualifiedPlays, b.ListeningMinutes,
        b.Height, b.Width, b.Color, b.Genres);

    public static GenerateCityRequest ToInternal(this CreateCityRequest r) =>
        new(r.Name, r.From, r.To, r.Layout, r.Recipe, r.Genres, r.Subgenres);

    public static GeneratedCityDto ToDto(this GeneratedCityInfo c) => new(
        c.CityId, c.Name, c.From, c.To, c.Layout, c.Recipe, c.Genres, c.Subgenres, c.Status, c.ErrorCode,
        c.CreatedAt, c.CompletedAt, c.Districts, c.Artists, c.TopGenres);

    public static GeneratedCityDetailDto ToDto(this GeneratedCityDetail d) => new(d.Info.ToDto(), d.City?.ToDto(), d.Map);

    public static GeneratedCityPageDto ToDto(this GeneratedCityPage p) =>
        new(p.Items.Select(i => i.ToDto()).ToList(), p.Page, p.PageSize, p.Total);

    public static GenreOptionDto ToDto(this GenreOption g) => new(
        g.GenreId, g.Name, g.Color, g.QualifiedPlays, g.Artists,
        g.Subgenres.Select(s => new SubgenreOptionDto(s.GenreId, s.Name, s.QualifiedPlays, s.Artists)).ToList());

    public static ImportStatusDto ToDto(this TaskInfo t) =>
        new(t.UploadId, t.Status, t.Inserted, t.StartedAt, t.CompletedAt, t.ErrorCode);

    public static ImportStatusDto ToDto(this SpotifyJobInfo j) =>
        new(j.JobId, j.Status, j.Inserted, j.StartedAt, j.CompletedAt, j.ErrorCode);

    public static PipelineRunDto ToDto(this PipelineRunInfo r) => new(
        r.RunId, r.Status, r.Triggers.Sum(t => t.NewPlays), r.Triggers.Select(t => t.Source).Distinct().ToList(),
        r.CreatedAt, r.StartedAt, r.CompletedAt, r.ErrorCode);
}
