using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CityFy.Retrieve.MusicBrainz.Models;
using Microsoft.Extensions.Logging;

namespace CityFy.Retrieve.MusicBrainz.Services;

public class GenreParser : AbstractDumpParser<GenreParser.ParsedResult>
{
    private readonly ICoreDumpParser _coreDumpParser;

    public GenreParser(ILogger<GenreParser> logger, IFileService? fileService = null, ICoreDumpParser? coreDumpParser = null)
        : base(logger, fileService)
    {
        _coreDumpParser = coreDumpParser ?? new CoreDumpParser();
    }

    public class ParsedResult
    {
        public List<Genre> Genres { get; set; } = new List<Genre>();
        public List<GenreRelation> Relations { get; set; } = new List<GenreRelation>();
        public Dictionary<long, Genre> GenresByDumpId { get; set; } = new Dictionary<long, Genre>();
    }
    protected override ParsedResult CreateResult() => new ParsedResult();

    protected override void HandleParsedTuple(ParsedResult result, ParsedTuple tuple)
    {
        if (!result.GenresByDumpId.ContainsKey(tuple.Id))
        {
            var g = new Genre { Name = tuple.Name ?? $"genre_{tuple.Id}", MusicBrainzId = tuple.Guid };
            result.GenresByDumpId[tuple.Id] = g;
            result.Genres.Add(g);
        }
    }

    protected override void HandleRelationParts(ParsedResult result, string[] parts)
    {
        foreach (var (a, b, rel) in EnumerateNumericPairs(parts))
        {
            if (!result.GenresByDumpId.TryGetValue(a, out var parent))
            {
                parent = new Genre { Name = $"genre_{a}" };
                result.GenresByDumpId[a] = parent;
                result.Genres.Add(parent);
            }
            if (!result.GenresByDumpId.TryGetValue(b, out var child))
            {
                child = new Genre { Name = $"genre_{b}" };
                result.GenresByDumpId[b] = child;
                result.Genres.Add(child);
            }

            var relType = rel ?? "subgenre";
            var relObj = new GenreRelation { Parent = parent, Child = child, RelationType = relType };
            result.Relations.Add(relObj);
        }
    }

    // Parse specific core dump files: genre, l_genre_genre, link, link_type
    public async Task<ParsedResult> ParseCoreDumpAsync(string extractedDirectory, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Parsing core dump files in {Directory}", extractedDirectory);
        var result = new ParsedResult();
        var genresById = new Dictionary<long, Genre>();

        string genreFile = Path.Combine(extractedDirectory, "mbdump", "genre");
        string lGenreGenreFile = Path.Combine(extractedDirectory, "mbdump", "l_genre_genre");
        string linkFile = Path.Combine(extractedDirectory, "mbdump", "link");
        string linkTypeFile = Path.Combine(extractedDirectory, "mbdump", "link_type");

        // fallback: try without mbdump/ prefix
        if (!File.Exists(genreFile)) genreFile = Path.Combine(extractedDirectory, "genre");
        if (!File.Exists(lGenreGenreFile)) lGenreGenreFile = Path.Combine(extractedDirectory, "l_genre_genre");
        if (!File.Exists(linkFile)) linkFile = Path.Combine(extractedDirectory, "link");
        if (!File.Exists(linkTypeFile)) linkTypeFile = Path.Combine(extractedDirectory, "link_type");

        // 1) parse genre file
        try
        {
            if (File.Exists(genreFile))
            {
                var lines = await _fileService.ReadWhitespaceSplitLinesAsync(genreFile, cancellationToken);
                var parsed = _coreDumpParser.ParseLines<List<string>>(lines, parts =>
                {
                    if (parts.Length < 2) return (false, null!);
                    return (true, parts.ToList());
                });

                foreach (var parts in parsed)
                {
                    if (parts == null || parts.Count == 0) continue;
                    // first numeric field is the id
                    var idPart = parts.FirstOrDefault(p => long.TryParse(p, out _));
                    if (idPart == null) continue;
                    if (!long.TryParse(idPart, out var id)) continue;
                    var name = parts.Skip(1).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
                    var gid = parts.FirstOrDefault(p => GuidRegex.IsMatch(p));
                    if (!genresById.ContainsKey(id))
                    {
                        var genre = new Genre { Name = name ?? $"genre_{id}", MusicBrainzId = gid };
                        genresById[id] = genre;
                        result.Genres.Add(genre);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse genre file {File}", genreFile);
        }

        // 2) parse link_type file to detect subgenre link types
        var subgenreLinkTypeIds = new HashSet<long>();
        try
        {
            if (File.Exists(linkTypeFile))
            {
                var lines = await _fileService.ReadWhitespaceSplitLinesAsync(linkTypeFile, cancellationToken);
                var rows = _coreDumpParser.ParseLines<List<string>>(lines, parts => (true, parts.ToList()));
                foreach (var parts in rows)
                {
                    if (parts == null || parts.Count < 2) continue;
                    if (long.TryParse(parts[0], out var id))
                    {
                        var name = parts.Skip(1).FirstOrDefault();
                        if (!string.IsNullOrEmpty(name) && name.IndexOf("subgenre", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            subgenreLinkTypeIds.Add(id);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse link_type file {File}", linkTypeFile);
        }

        // 3) parse link table to map linkId -> linkTypeId
        var linkMap = new Dictionary<long, long>();
        try
        {
            if (File.Exists(linkFile))
            {
                var lines = await _fileService.ReadWhitespaceSplitLinesAsync(linkFile, cancellationToken);
                linkMap = _coreDumpParser.ParseDictionaryFromLines<long>(lines, parts =>
                {
                    if (parts.Length < 2) return (false, 0L, 0L);
                    if (!long.TryParse(parts[0], out var id)) return (false, 0L, 0L);
                    foreach (var p in parts.Skip(1))
                    {
                        if (long.TryParse(p, out var lt))
                        {
                            return (true, id, lt);
                        }
                    }
                    return (false, 0L, 0L);
                });
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
                var lines = await _fileService.ReadWhitespaceSplitLinesAsync(lGenreGenreFile, cancellationToken);
                var numericRows = _coreDumpParser.ParseLines<List<long>>(lines, parts =>
                {
                    var nums = parts
                        .Select(p => { long v; return long.TryParse(p, out v) ? (long?)v : null; })
                        .Where(v => v.HasValue)
                        .Select(v => v!.Value)
                        .ToList();
                    if (nums.Count == 0) return (false, null!);
                    return (true, nums);
                });

                foreach (var nums in numericRows)
                {
                    if (nums == null || nums.Count < 2) continue;
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

}
