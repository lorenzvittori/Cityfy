using CityFy.Retrieve.MusicBrainz.Data;
using CityFy.Retrieve.MusicBrainz.Models;
using Microsoft.EntityFrameworkCore;

namespace CityFy.Retrieve.MusicBrainz.SubServices;

public sealed class GenreGraphSubService(MusicBrainzContext db) : IGenreGraphSubService
{    public async Task<GenreGraph> GetGraphAsync(CancellationToken ct)
    {
        var genres = await db.Genres.OrderBy(g => g.Name)
            .Select(g => new GenreNode(g.Id, g.Gid, g.Name)).ToListAsync(ct);
        var relations = await db.GenreRelations
            .Select(r => new GenreEdge(r.FromGenreId, r.ToGenreId, r.RelationType)).ToListAsync(ct);
        return new GenreGraph(genres, relations);
    }

    /// <summary>
    /// Top-level ancestors of a genre following "subgenre" edges upwards (a genre can have more
    /// than one parent). A genre without parents is its own root. Null if the genre does not exist.
    /// </summary>
    public async Task<IReadOnlyList<GenreNode>?> GetRootsAsync(int genreId, CancellationToken ct)
    {
        if (!await db.Genres.AnyAsync(g => g.Id == genreId, ct))
            return null;

        var parentsOf = (await db.GenreRelations
                .Where(r => r.RelationType == GenreRelationTypes.Subgenre)
                .Select(r => new { Parent = r.FromGenreId, Child = r.ToGenreId })
                .ToListAsync(ct))
            .ToLookup(r => r.Child, r => r.Parent);

        var roots = new HashSet<int>();
        var visited = new HashSet<int> { genreId };
        var queue = new Queue<int>([genreId]);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var parents = parentsOf[current].ToList();
            if (parents.Count == 0) roots.Add(current);
            // visited guards against cycles in community-edited data
            foreach (var p in parents.Where(visited.Add)) queue.Enqueue(p);
        }

        return await db.Genres.Where(g => roots.Contains(g.Id)).OrderBy(g => g.Name)
            .Select(g => new GenreNode(g.Id, g.Gid, g.Name)).ToListAsync(ct);
    }
}
