namespace CityFy.Catalog.Models;

/// <summary>
/// Genres kept in a city: an artist is kept when its district is in Genres or its block is in
/// Subgenres. Both empty = every artist, including those without a genre (reported as unresolved).
/// </summary>
public sealed class GenreFilter
{
    public static readonly GenreFilter All = new([], []);

    private readonly HashSet<int> _genres;
    private readonly HashSet<int> _subgenres;

    public GenreFilter(IEnumerable<int>? genres, IEnumerable<int>? subgenres)
    {
        _genres = genres?.ToHashSet() ?? [];
        _subgenres = subgenres?.ToHashSet() ?? [];
    }

    public bool IsAll => _genres.Count == 0 && _subgenres.Count == 0;

    public bool Includes(ArtistPlacement artist) =>
        IsAll || (artist.IsPlaced && (_genres.Contains(artist.DistrictGenreId!.Value) || _subgenres.Contains(artist.BlockGenreId!.Value)));
}
