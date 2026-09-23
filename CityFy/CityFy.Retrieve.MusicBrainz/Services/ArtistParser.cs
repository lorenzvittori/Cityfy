using System.Text.RegularExpressions;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using CityFy.Retrieve.MusicBrainz.Models;
using Microsoft.Extensions.Logging;

namespace CityFy.Retrieve.MusicBrainz.Services;

public class ArtistParser : AbstractDumpParser<ArtistParser.ParsedResult>
{
    public class ParsedResult
    {
        public List<Artist> Artists { get; set; } = new List<Artist>();
        public List<ArtistGenreRelation> Relations { get; set; } = new List<ArtistGenreRelation>();
        public Dictionary<long, Artist> ArtistsByDumpId { get; set; } = new Dictionary<long, Artist>();
    }

    private readonly ICoreDumpParser _coreDumpParser;

    public ArtistParser(ILogger<ArtistParser> logger, IFileService? fileService = null, ICoreDumpParser? coreDumpParser = null) : base(logger, fileService)
    {
        _coreDumpParser = coreDumpParser ?? new CoreDumpParser();
    }

    protected override ParsedResult CreateResult() => new ParsedResult();

    protected override void HandleParsedTuple(ParsedResult result, ParsedTuple tuple)
    {
        if (!result.ArtistsByDumpId.ContainsKey(tuple.Id))
        {
            var a = new Artist { Id = (int)tuple.Id, Name = tuple.Name ?? $"artist_{tuple.Id}", MusicBrainzId = tuple.Guid };
            result.ArtistsByDumpId[tuple.Id] = a;
            result.Artists.Add(a);
        }
    }

    protected override void HandleRelationParts(ParsedResult result, string[] parts)
    {
        foreach (var (a, b, rel) in EnumerateNumericPairs(parts))
        {
            if (result.ArtistsByDumpId.ContainsKey(a))
            {
                result.Relations.Add(new ArtistGenreRelation { ArtistId = (int)a, GenreId = (int)b, RelationType = rel ?? "tag" });
            }
        }
    }

    public async Task<ParsedResult> ParseCoreDumpAsync(string extractedDirectory, GenreParser.ParsedResult? genresParsed = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Parsing artist core dump files in {Directory}", extractedDirectory);
        var result = new ParsedResult();

        string artistFile = Path.Combine(extractedDirectory, "mbdump", "artist");
        if (!File.Exists(artistFile)) artistFile = Path.Combine(extractedDirectory, "artist");

        try
        {
            if (File.Exists(artistFile))
            {
                var lines = await _fileService.ReadWhitespaceSplitLinesAsync(artistFile, cancellationToken);
                var parsed = _coreDumpParser.ParseLines<List<string>>(lines, parts =>
                {
                    if (parts.Length < 2) return (false, null!);
                    return (true, parts.ToList());
                });

                foreach (var parts in parsed)
                {
                    if (parts == null || parts.Count == 0) continue;
                    var idPart = parts.FirstOrDefault(p => long.TryParse(p, out _));
                    if (idPart == null) continue;
                    if (!long.TryParse(idPart, out var id)) continue;

                    // GUID: first token that matches GUID pattern
                    var gidToken = parts.FirstOrDefault(p => GuidRegex.IsMatch(p));
                    var gid = gidToken?.Trim('"', '\'') ;

                    // Name: prefer a token that is not numeric, not a GUID and not the placeholder \N
                    string? name = parts.Skip(1)
                        .Select(p => p.Trim('"', '\''))
                        .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && p != "\\N" && !GuidRegex.IsMatch(p) && !long.TryParse(p, out _));

                    // Fallback: if no sensible name found, try tokens that are not numeric
                    if (string.IsNullOrEmpty(name))
                    {
                        name = parts.Skip(1)
                            .Select(p => p.Trim('"', '\''))
                            .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && p != "\\N" && !long.TryParse(p, out _));
                    }

                    // Final fallback: use GUID or generated placeholder
                    var finalName = !string.IsNullOrEmpty(name) ? name : gid ?? $"artist_{id}";

                    if (!result.ArtistsByDumpId.ContainsKey(id))
                    {
                        var artist = new Artist { Id = (int)id, Name = finalName, MusicBrainzId = gid };
                        result.ArtistsByDumpId[id] = artist;
                        result.Artists.Add(artist);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse artist file {File}", artistFile);
        }

        // parse link_type -> id->name mapping (optional)
        var linkTypeNames = new Dictionary<long, string>();
        string linkTypeFile = Path.Combine(extractedDirectory, "mbdump", "link_type");
        if (!File.Exists(linkTypeFile)) linkTypeFile = Path.Combine(extractedDirectory, "link_type");
        try
        {
            if (File.Exists(linkTypeFile))
            {
                var lines = await _fileService.ReadWhitespaceSplitLinesAsync(linkTypeFile, cancellationToken);
                var rows = _coreDumpParser.ParseLines<List<string>>(lines, parts => (true, parts.ToList()));
                foreach (var parts in rows)
                {
                    if (parts == null || parts.Count < 2) continue;
                    if (!long.TryParse(parts[0], out var id)) continue;
                    var name = parts.Skip(1).FirstOrDefault();
                    if (!string.IsNullOrEmpty(name)) linkTypeNames[id] = name;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse link_type file {File}", linkTypeFile);
        }

        // parse link -> link_type mapping
        var linkMap = new Dictionary<long, long>();
        string linkFile = Path.Combine(extractedDirectory, "mbdump", "link");
        if (!File.Exists(linkFile)) linkFile = Path.Combine(extractedDirectory, "link");
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

        // parse l_artist_genre to create artist-genre relations
        string lArtistGenreFile = Path.Combine(extractedDirectory, "mbdump", "l_artist_genre");
        if (!File.Exists(lArtistGenreFile)) lArtistGenreFile = Path.Combine(extractedDirectory, "l_artist_genre");
        try
        {
            if (File.Exists(lArtistGenreFile))
            {
                var lines = await _fileService.ReadWhitespaceSplitLinesAsync(lArtistGenreFile, cancellationToken);
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
                    long? artistNum = null;
                    long? genreNum = null;

                    // pick numbers known as artists
                    var artistNums = nums.Where(n => result.ArtistsByDumpId.ContainsKey(n)).ToList();
                    if (artistNums.Count >= 1) artistNum = artistNums[0];

                    // pick numbers known as genres from provided genresParsed
                    var genreNums = genresParsed != null ? nums.Where(n => genresParsed.GenresByDumpId.ContainsKey(n)).ToList() : new List<long>();
                    if (genreNums.Count >= 1) genreNum = genreNums[0];

                    // fallback: pick any number that's not the artist
                    if (!genreNum.HasValue)
                    {
                        var others = nums.Where(n => n != artistNum && n != linkId).ToList();
                        if (others.Count >= 1) genreNum = others[0];
                    }

                    if (!artistNum.HasValue || !genreNum.HasValue) continue;

                    string relType = "tag";
                    if (linkId.HasValue && linkMap.TryGetValue(linkId.Value, out var lt) && linkTypeNames.TryGetValue(lt, out var ltName))
                    {
                        relType = ltName ?? "tag";
                    }

                    result.Relations.Add(new ArtistGenreRelation { ArtistId = (int)artistNum.Value, GenreId = (int)genreNum.Value, RelationType = relType });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse l_artist_genre file {File}", lArtistGenreFile);
        }

        // parse tag and artist_tag to capture artist -> tag associations (tags may correspond to genres)
        if (genresParsed != null)
        {
            try
            {
                // build genre name -> dumpId map for quick lookup
                var genreNameToDumpId = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in genresParsed.GenresByDumpId)
                {
                    if (kv.Value?.Name != null && !genreNameToDumpId.ContainsKey(kv.Value.Name))
                    {
                        genreNameToDumpId[kv.Value.Name] = kv.Key;
                    }
                }

                // parse tag file: tag id -> tag name
                var tagMap = new Dictionary<long, string>();
                string tagFile = Path.Combine(extractedDirectory, "mbdump", "tag");
                if (!File.Exists(tagFile)) tagFile = Path.Combine(extractedDirectory, "tag");
                if (File.Exists(tagFile))
                {
                    var lines = await _fileService.ReadWhitespaceSplitLinesAsync(tagFile, cancellationToken);
                    var rows = _coreDumpParser.ParseLines<List<string>>(lines, parts => (true, parts.ToList()));
                    foreach (var parts in rows)
                    {
                        if (parts == null || parts.Count < 2) continue;
                        if (!long.TryParse(parts[0], out var tid)) continue;
                        // name usually in subsequent fields
                        var name = parts.Skip(1).Select(p => p.Trim('"', '\''))
                                       .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && p != "\\N");
                        if (!string.IsNullOrEmpty(name)) tagMap[tid] = name!;
                    }
                }

                // parse artist_tag file: map artist dump id -> tag id
                string artistTagFile = Path.Combine(extractedDirectory, "mbdump", "artist_tag");
                if (!File.Exists(artistTagFile)) artistTagFile = Path.Combine(extractedDirectory, "artist_tag");
                if (File.Exists(artistTagFile))
                {
                    var lines = await _fileService.ReadWhitespaceSplitLinesAsync(artistTagFile, cancellationToken);
                    var numericRows = _coreDumpParser.ParseLines<List<long>>(lines, parts =>
                    {
                        var nums = parts.Select(p => { long v; return long.TryParse(p, out v) ? (long?)v : null; })
                                         .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                        if (nums.Count == 0) return (false, null!);
                        return (true, nums);
                    });

                    foreach (var nums in numericRows)
                    {
                        if (nums == null || nums.Count < 2) continue;
                        // common layout: artist, tag, count, ... -> take first two numeric as artistId and tagId
                        var artistNum = nums[0];
                        var tagId = nums.Count > 1 ? nums[1] : (long?)null;
                        if (tagId == null) continue;

                        if (!result.ArtistsByDumpId.ContainsKey(artistNum)) continue;
                        if (!tagMap.TryGetValue(tagId.Value, out var tagName)) continue;
                        if (!genreNameToDumpId.TryGetValue(tagName, out var genreDumpId)) continue;

                        // avoid duplicates
                        if (!result.Relations.Any(r => r.ArtistId == (int)artistNum && r.GenreId == (int)genreDumpId))
                        {
                            result.Relations.Add(new ArtistGenreRelation { ArtistId = (int)artistNum, GenreId = (int)genreDumpId, RelationType = "tag" });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse tag/artist_tag files in {Dir}", extractedDirectory);
            }
        }

        return result;
    }
}
