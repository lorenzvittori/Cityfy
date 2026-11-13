namespace CityFy.Catalog.Models;

/// <summary>
/// Subgenre hierarchy of the MusicBrainz genre graph: for every genre, its top-level
/// ancestors ("roots"), which become the city districts. Only "subgenre" edges are followed
/// ("From has subgenre To"); a genre can have several roots, a genre without parents is its
/// own root.
/// </summary>
public sealed class GenreHierarchy
{
    public const string SubgenreRelation = "subgenre";

    private readonly Dictionary<int, GenreNode> _genres;
    private readonly ILookup<int, int> _parents;
    private readonly Dictionary<int, IReadOnlyList<int>> _rootsCache = new();

    public GenreHierarchy(GenreGraph graph)
    {
        _genres = graph.Genres.ToDictionary(g => g.Id);
        _parents = graph.Relations
            .Where(IsSubgenre)
            .ToLookup(r => r.ToGenreId, r => r.FromGenreId);
    }

    private static bool IsSubgenre(GenreEdge relation) => relation.Type == SubgenreRelation;

    public GenreNode? Get(int genreId) => _genres.GetValueOrDefault(genreId);

    public IReadOnlyList<int> GetRoots(int genreId)
    {
        if (_rootsCache.TryGetValue(genreId, out var cached))
            return cached;

        var roots = new List<int>();
        var visited = new HashSet<int> { genreId };
        var queue = new Queue<int>([genreId]);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var parents = _parents[current].ToList();
            if (parents.Count == 0)
                roots.Add(current);
            // visited guards against cycles in community-edited data
            foreach (var p in parents.Where(visited.Add))
                queue.Enqueue(p);
        }

        // A cycle with no exit leaves no root: fall back to the genre itself
        if (roots.Count == 0)
            roots.Add(genreId);

        roots.Sort();
        return _rootsCache[genreId] = roots;
    }
}
