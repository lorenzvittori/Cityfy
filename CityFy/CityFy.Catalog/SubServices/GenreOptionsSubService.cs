using CityFy.Catalog.Models;
using ServiceDefault.City;

namespace CityFy.Catalog.SubServices;

public sealed class GenreOptionsSubService : IGenreOptionsSubService
{
    public IReadOnlyList<GenreOption> List(IReadOnlyList<ArtistPlays> plays, IReadOnlyDictionary<string, ArtistPlacement> placementsByArtistName)
    {
        var placed = plays
            .Where(p => p.QualifiedPlays > 0)
            .Select(p => (Plays: p, Artist: placementsByArtistName.GetValueOrDefault(p.ArtistName)))
            .Where(x => x.Artist is { IsPlaced: true })
            .ToList();

        return placed
            .GroupBy(x => x.Artist!.DistrictGenreId!.Value)
            .Select(d => new GenreOption(
                d.Key,
                d.First().Artist!.DistrictName!,
                d.First().Artist!.DistrictColor!,
                d.Sum(x => x.Plays.QualifiedPlays),
                d.Select(x => x.Artist!.Mbid).Distinct().Count(),
                d.GroupBy(x => x.Artist!.BlockGenreId!.Value)
                    .Select(b => new SubgenreOption(
                        b.Key,
                        b.First().Artist!.BlockName!,
                        b.Sum(x => x.Plays.QualifiedPlays),
                        b.Select(x => x.Artist!.Mbid).Distinct().Count()))
                    .OrderByDescending(s => s.QualifiedPlays).ThenBy(s => s.Name, StringComparer.Ordinal)
                    .ToList()))
            .OrderByDescending(g => g.QualifiedPlays).ThenBy(g => g.Name, StringComparer.Ordinal)
            .ToList();
    }
}
