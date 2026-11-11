using CityFy.Api.Clients;
using ServiceDefault.City;

namespace CityFy.Api.Models;

/// <summary>Internal documents -> public v1 DTOs.</summary>
public static class V1Mapper
{
    public static CityDto ToDto(this CityModel c) => new(
        c.Period,
        c.GeneratedAt,
        c.From,
        new CitySummaryDto(c.Summary.TotalPlays, c.Summary.QualifiedPlays, c.Summary.ListeningHours,
            c.Summary.TotalArtists, c.Summary.PlacedArtists, c.Summary.CoveragePercent, c.Summary.TopGenres),
        c.Warnings,
        c.Districts.Select(ToDto).ToList());

    public static DistrictDto ToDto(this CityDistrict d) => new(
        d.DistrictId, d.Name, d.Color, d.Density, d.ListeningIntensity, d.Score,
        d.Blocks.Select(b => new BlockDto(b.BlockId, b.GenreId, b.Name, b.Score, b.Buildings.Select(ToDto).ToList())).ToList());

    public static DistrictSummaryDto ToSummaryDto(this CityDistrict d) => new(
        d.DistrictId, d.Name, d.Color, d.Density, d.ListeningIntensity, d.Score,
        d.Blocks.Count, d.Blocks.Sum(b => b.Buildings.Count));

    public static BuildingDto ToDto(this CityBuilding b) => new(
        b.BuildingId, b.Name, b.SpotifyArtistUri, b.Plays, b.QualifiedPlays, b.ListeningMinutes,
        b.Height, b.Width, b.Color, b.Genres);

    public static GenreDto ToGenreDto(this CityBlock b, CityDistrict d) => new(
        b.GenreId, b.Name, d.DistrictId, d.Name, d.Color, b.Score, b.Buildings.Count);

    public static ImportStatusDto ToDto(this TaskInfo t) =>
        new(t.UploadId, t.Status, t.Inserted, t.StartedAt, t.CompletedAt, t.ErrorCode);

    public static ImportStatusDto ToDto(this SpotifyJobInfo j) =>
        new(j.JobId, j.Status, j.Inserted, j.StartedAt, j.CompletedAt, j.ErrorCode);

    public static PipelineRunDto ToDto(this PipelineRunInfo r) => new(
        r.RunId, r.Status, r.Triggers.Sum(t => t.NewPlays), r.Triggers.Select(t => t.Source).Distinct().ToList(),
        r.CreatedAt, r.StartedAt, r.CompletedAt, r.ErrorCode);
}
