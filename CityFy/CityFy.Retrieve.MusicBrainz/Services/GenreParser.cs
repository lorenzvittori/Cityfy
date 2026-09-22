using System.Text.RegularExpressions;
using CityFy.Retrieve.MusicBrainz.Models;
using Microsoft.Extensions.Logging;

namespace CityFy.Retrieve.MusicBrainz.Services;

public class GenreParser
{
    private static readonly Regex InsertValuesRegex = new(@"\(([^\)]+)\)", RegexOptions.Compiled);
    private static readonly Regex GuidRegex = new(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.Compiled);
    private readonly ILogger<GenreParser> _logger;

    public GenreParser(ILogger<GenreParser> logger)
    {
        _logger = logger;
    }

    public class ParsedResult
    {
        public List<Genre> Genres { get; set; } = new List<Genre>();
        public List<GenreRelation> Relations { get; set; } = new List<GenreRelation>();
    }

    // Scans la directory estratta e prova a costruire lista di generi e relazioni.
    public async Task<ParsedResult> ParseAsync(string extractedDirectory, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Parsing directory {Directory}", extractedDirectory);

        var genresById = new Dictionary<long, Genre>();
        var relations = new List<GenreRelation>();

        var files = Directory.EnumerateFiles(extractedDirectory, "*.*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        _logger.LogInformation("Found {Count} candidate files to scan", files.Count);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string text;
            try
            {
                text = await File.ReadAllTextAsync(file, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read file {File}; skipping", file);
                continue;
            }

            // Parse genre inserts
            if (text.IndexOf("INSERT INTO", StringComparison.OrdinalIgnoreCase) >= 0 && text.IndexOf("genre", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // find tuples
                foreach (Match m in InsertValuesRegex.Matches(text))
                {
                    var tuple = m.Groups[1].Value;
                    var parts = SplitTupleRespectingQuotes(tuple);
                    if (parts.Length == 0) continue;

                    // try interpret first as id
                    if (long.TryParse(parts[0], out var id))
                    {
                        var name = parts.FirstOrDefault(p => p.StartsWith("'") && p.EndsWith("'"));
                        if (name != null)
                        {
                            name = name.Trim('\'', '"');
                        }
                        else
                        {
                            var q = parts.FirstOrDefault(p => p.Contains("'") || p.Contains('"'));
                            if (q != null)
                                name = q.Trim('\'', '"');
                        }

                        var gid = parts.FirstOrDefault(p => GuidRegex.IsMatch(p));
                        gid = gid?.Trim('\'', '"');

                        if (!genresById.ContainsKey(id))
                        {
                            var g = new Genre { Name = name ?? $"genre_{id}", MusicBrainzId = gid };
                            genresById[id] = g;
                        }
                    }
                }
            }

            // Try parse relations: look for tuples likely containing two numeric ids
            if (text.IndexOf("relation", StringComparison.OrdinalIgnoreCase) >= 0 && text.IndexOf("genre", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                foreach (Match m in InsertValuesRegex.Matches(text))
                {
                    var tuple = m.Groups[1].Value;
                    var parts = SplitTupleRespectingQuotes(tuple);
                    if (parts.Length < 2) continue;
                    if (long.TryParse(parts[0], out var parentId) && long.TryParse(parts[1], out var childId))
                    {
                        // create placeholder genres if missing
                        if (!genresById.TryGetValue(parentId, out var parent))
                        {
                            parent = new Genre { Name = $"genre_{parentId}" };
                            genresById[parentId] = parent;
                        }
                        if (!genresById.TryGetValue(childId, out var child))
                        {
                            child = new Genre { Name = $"genre_{childId}" };
                            genresById[childId] = child;
                        }

                        var rel = new GenreRelation { Parent = parent, Child = child, RelationType = parts.Length >= 3 ? parts[2].Trim('\'', '"') : "subgenre" };
                        relations.Add(rel);
                    }
                }
            }
        }

        var result = new ParsedResult { Genres = genresById.Values.ToList(), Relations = relations };
        _logger.LogInformation("Parsing complete: {Genres} genres, {Relations} relations found", result.Genres.Count, result.Relations.Count);
        if (result.Genres.Count > 0)
        {
            var sample = string.Join(", ", result.Genres.Take(10).Select(g => g.Name));
            _logger.LogInformation("Sample genres: {Sample}", sample);
        }

        return result;
    }

    // Parse specific core dump files: genre, l_genre_genre, link, link_type
    public async Task<ParsedResult> ParseCoreDumpAsync(string extractedDirectory, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Parsing core dump files in {Directory}", extractedDirectory);
        var result = new ParsedResult();

        string genreFile = Path.Combine(extractedDirectory, "mbdump", "genre");
        string lGenreGenreFile = Path.Combine(extractedDirectory, "mbdump", "l_genre_genre");
        string linkFile = Path.Combine(extractedDirectory, "mbdump", "link");
        string linkTypeFile = Path.Combine(extractedDirectory, "mbdump", "link_type");

        // fallback: try without mbdump/ prefix
        if (!File.Exists(genreFile)) genreFile = Path.Combine(extractedDirectory, "genre");
        if (!File.Exists(lGenreGenreFile)) lGenreGenreFile = Path.Combine(extractedDirectory, "l_genre_genre");
        if (!File.Exists(linkFile)) linkFile = Path.Combine(extractedDirectory, "link");
        if (!File.Exists(linkTypeFile)) linkTypeFile = Path.Combine(extractedDirectory, "link_type");

        if (!File.Exists(genreFile))
        {
            _logger.LogWarning("Genre file not found at {Path}; falling back to generic parsing", genreFile);
            return await ParseAsync(extractedDirectory, cancellationToken);
        }

        // 1) parse genres
        var genresById = new Dictionary<long, Genre>();
        try
        {
            var text = await File.ReadAllTextAsync(genreFile, cancellationToken);
            foreach (Match m in InsertValuesRegex.Matches(text))
            {
                var tuple = m.Groups[1].Value;
                var parts = SplitTupleRespectingQuotes(tuple);
                if (parts.Length == 0) continue;
                if (!long.TryParse(parts[0], out var id)) continue;
                string? name = null;
                string? gid = null;
                // find quoted name
                foreach (var p in parts)
                {
                    var t = p.Trim();
                    if (t.StartsWith("'") && t.EndsWith("'"))
                    {
                        var val = t.Trim('\'', '"');
                        if (name == null) name = val;
                    }
                    var gmatch = GuidRegex.Match(p);
                    if (gmatch.Success) gid = gmatch.Value;
                }
                var g = new Genre { Name = name ?? $"genre_{id}", MusicBrainzId = gid };
                // temporarily store with Id in a separate dictionary; actual EF Id will be assigned later
                genresById[id] = g;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse genre file {File}", genreFile);
            return await ParseAsync(extractedDirectory, cancellationToken);
        }

        result.Genres = genresById.Values.ToList();

        // 2) parse link_type to find GUIDs for 'subgenre' (prefer to detect dynamically, fallback to known GUID)
        var subgenreLinkTypeIds = new HashSet<long>();
        try
        {
            if (File.Exists(linkTypeFile))
            {
                var text = await File.ReadAllTextAsync(linkTypeFile, cancellationToken);
                foreach (Match m in InsertValuesRegex.Matches(text))
                {
                    var tuple = m.Groups[1].Value;
                    var parts = SplitTupleRespectingQuotes(tuple);
                    if (parts.Length < 2) continue;
                    if (!long.TryParse(parts[0], out var id)) continue;
                    // search for name or gid
                    var name = parts.Skip(1).FirstOrDefault(p => p.Contains("'"));
                    var gid = parts.Skip(1).Select(p => GuidRegex.Match(p)).FirstOrDefault(gm => gm.Success)?.Value;
                    if (!string.IsNullOrEmpty(gid) && gid.Equals("9d61bc67-fa39-4719-8025-ea056a5bd7e6", StringComparison.OrdinalIgnoreCase))
                    {
                        subgenreLinkTypeIds.Add(id);
                    }
                    else if (name != null && name.IndexOf("subgenre", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        subgenreLinkTypeIds.Add(id);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse link_type file {File}", linkTypeFile);
        }
        // if not found, use fallback GUID detection in link table later

        // 3) parse link table to map linkId -> linkTypeId
        var linkMap = new Dictionary<long, long>();
        try
        {
            if (File.Exists(linkFile))
            {
                var text = await File.ReadAllTextAsync(linkFile, cancellationToken);
                foreach (Match m in InsertValuesRegex.Matches(text))
                {
                    var tuple = m.Groups[1].Value;
                    var parts = SplitTupleRespectingQuotes(tuple);
                    if (parts.Length < 2) continue;
                    if (!long.TryParse(parts[0], out var id)) continue;
                    // try to find link_type id among parts
                    foreach (var p in parts.Skip(1))
                    {
                        if (long.TryParse(p, out var lt))
                        {
                            // heuristics: if this lt looks like a link_type id, map it
                            if (!linkMap.ContainsKey(id)) linkMap[id] = lt;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse link file {File}", linkFile);
        }

        // 4) parse l_genre_genre and create relations when link_type matches subgenre
        try
        {
            if (File.Exists(lGenreGenreFile))
            {
                var text = await File.ReadAllTextAsync(lGenreGenreFile, cancellationToken);
                foreach (Match m in InsertValuesRegex.Matches(text))
                {
                    var tuple = m.Groups[1].Value;
                    var parts = SplitTupleRespectingQuotes(tuple);
                    var nums = parts.Select(p => { long v; return long.TryParse(p, out v) ? (long?)v : null; }).Where(v => v.HasValue).Select(v => v!.Value).ToList();
                    if (nums.Count < 2) continue;

                    // find which num corresponds to a link id (present in linkMap)
                    long? linkId = nums.FirstOrDefault(n => linkMap.ContainsKey(n));
                    long? genreA = null;
                    long? genreB = null;

                    // pick two nums that are known genres
                    var genreNums = nums.Where(n => genresById.ContainsKey(n)).ToList();
                    if (genreNums.Count >= 2)
                    {
                        genreA = genreNums[0];
                        genreB = genreNums[1];
                    }
                    else
                    {
                        // fallback: try nums excluding linkId
                        var others = nums.Where(n => n != linkId).ToList();
                        if (others.Count >= 2)
                        {
                            genreA = others[0];
                            genreB = others[1];
                        }
                    }

                    if (!genreA.HasValue || !genreB.HasValue) continue;

                    // determine link type id
                    long? ltid = null;
                    if (linkId.HasValue && linkMap.TryGetValue(linkId.Value, out var foundLt)) ltid = foundLt;

                    bool isSubgenre = false;
                    if (ltid.HasValue && subgenreLinkTypeIds.Contains(ltid.Value)) isSubgenre = true;

                    // if subgenre detection not possible yet, try to inspect link_type GUID via linkMap->link_type and fallback GUID
                    if (!isSubgenre && !subgenreLinkTypeIds.Any() && ltid.HasValue)
                    {
                        // no link_type parsed; attempt to detect by scanning link_type file for GUID 9d61...
                        if (File.Exists(linkTypeFile))
                        {
                            var lttext = await File.ReadAllTextAsync(linkTypeFile, cancellationToken);
                            if (lttext.Contains("9d61bc67-fa39-4719-8025-ea056a5bd7e6", StringComparison.OrdinalIgnoreCase))
                            {
                                // assume ltid corresponds to subgenre
                                isSubgenre = true;
                            }
                        }
                    }

                    if (isSubgenre)
                    {
                        var parent = genresById[genreA.Value];
                        var child = genresById[genreB.Value];
                        var rel = new GenreRelation { Parent = parent, Child = child, RelationType = "subgenre" };
                        result.Relations.Add(rel);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse l_genre_genre file {File}", lGenreGenreFile);
        }

        _logger.LogInformation("Core dump parsing complete: {Genres} genres, {Relations} relations found", result.Genres.Count, result.Relations.Count);
        return result;
    }

    private static string[] SplitTupleRespectingQuotes(string tuple)
    {
        var parts = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool inSingle = false;
        bool inDouble = false;
        for (int i = 0; i < tuple.Length; i++)
        {
            var c = tuple[i];
            if (c == '\'' && !inDouble)
            {
                inSingle = !inSingle;
                cur.Append(c);
                continue;
            }
            if (c == '"' && !inSingle)
            {
                inDouble = !inDouble;
                cur.Append(c);
                continue;
            }
            if (c == ',' && !inSingle && !inDouble)
            {
                parts.Add(cur.ToString().Trim());
                cur.Clear();
                continue;
            }
            cur.Append(c);
        }
        if (cur.Length > 0) parts.Add(cur.ToString().Trim());
        return parts.ToArray();
    }
}
