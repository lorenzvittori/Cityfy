using System.Text.RegularExpressions;
using CityFy.Retrieve.MusicBrainz.Models;

namespace CityFy.Retrieve.MusicBrainz.Services;

public interface ICoreDumpParser
{
    /// <summary>
    /// Parse genre lines from core-dump style files where each line is splitted into columns (id, guid, name, ...).
    /// Returns a dictionary mapping original id -> Genre.
    /// </summary>
    Dictionary<long, Genre> ParseGenresFromLines(IEnumerable<string[]> lines);

    /// <summary>
    /// Generic: parse lines into a dictionary using a mapper that extracts a key and a value from the columns.
    /// </summary>
    Dictionary<long, T> ParseDictionaryFromLines<T>(IEnumerable<string[]> lines, Func<string[], (bool success, long key, T value)> mapper);

    /// <summary>
    /// Generic: parse lines into a sequence of values using a mapper that returns success and value.
    /// </summary>
    IEnumerable<T> ParseLines<T>(IEnumerable<string[]> lines, Func<string[], (bool success, T value)> mapper);
}

public class CoreDumpParser : ICoreDumpParser
{
    private static readonly Regex GuidRegex = new(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.Compiled);

    public Dictionary<long, Genre> ParseGenresFromLines(IEnumerable<string[]> lines)
    {
        return ParseDictionaryFromLines<Genre>(lines, parts =>
        {
            if (parts == null || parts.Length < 3) return (false, 0L, null!);
            if (!long.TryParse(parts[0], out var id)) return (false, 0L, null!);
            var gidCandidate = parts[1].Trim('\'','\"');
            string? gid = GuidRegex.IsMatch(gidCandidate) ? gidCandidate : null;
            var name = parts[2].Trim('\'','\"');
            if (string.IsNullOrWhiteSpace(name)) name = $"genre_{id}";
            return (true, id, new Genre { Name = name, MusicBrainzId = gid });
        });
    }

    public Dictionary<long, T> ParseDictionaryFromLines<T>(IEnumerable<string[]> lines, Func<string[], (bool success, long key, T value)> mapper)
    {
        var dict = new Dictionary<long, T>();
        foreach (var parts in lines)
        {
            try
            {
                var res = mapper(parts);
                if (!res.success) continue;
                dict[res.key] = res.value;
            }
            catch
            {
                // ignore malformed lines
            }
        }
        return dict;
    }

    public IEnumerable<T> ParseLines<T>(IEnumerable<string[]> lines, Func<string[], (bool success, T value)> mapper)
    {
        var list = new List<T>();
        foreach (var parts in lines)
        {
            try
            {
                var res = mapper(parts);
                if (!res.success) continue;
                list.Add(res.value);
            }
            catch
            {
                // ignore
            }
        }
        return list;
    }
}
