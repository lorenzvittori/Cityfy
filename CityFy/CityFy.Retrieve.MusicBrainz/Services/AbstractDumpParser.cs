using System.Text.RegularExpressions;
using System.Text;
using Microsoft.Extensions.Logging;

namespace CityFy.Retrieve.MusicBrainz.Services;

// Abstract base parser containing shared helpers and configuration.
public abstract class AbstractDumpParser<TResult>
{
    protected static readonly Regex InsertValuesRegex = new(@"\(([^)]+)\)", RegexOptions.Compiled);
    protected static readonly Regex GuidRegex = new(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.Compiled);

    protected readonly ILogger _logger;
    protected readonly IFileService _fileService;

    // configuration parameters (paths, patterns, etc.) can be provided by derived classes via ctor
    protected AbstractDumpParser(ILogger logger, IFileService? fileService = null)
    {
        _logger = logger;
        _fileService = fileService ?? new FileService();
    }

    // Derived parsers implement handlers used by the common ParseAsync below.

    protected abstract TResult CreateResult();

    // Handle a parsed tuple (id, name, guid, parts)
    protected abstract void HandleParsedTuple(TResult result, ParsedTuple tuple);

    // Handle a tuple's parts when used to represent relations. Default no-op.
    protected virtual void HandleRelationParts(TResult result, string[] parts) { }

    // Common ParseAsync implemented in the abstract base. It enumerates files, reads text,
    // extracts tuples via EnumerateParsedTuplesFromText and delegates handling to derived classes.
    public async Task<TResult> ParseAsync(string extractedDirectory, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Parsing directory {Directory}", extractedDirectory);

        var result = CreateResult();

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
                text = await _fileService.ReadAllTextAsync(file, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read file {File}; skipping", file);
                continue;
            }

            // handle parsed tuples (id/name/guid)
            foreach (var pt in EnumerateParsedTuplesFromText(text))
            {
                HandleParsedTuple(result, pt);
            }

            // handle relation-like tuples: forward raw parts to derived parser
            foreach (Match m in InsertValuesRegex.Matches(text))
            {
                var tuple = m.Groups[1].Value;
                var parts = SplitTupleRespectingQuotes(tuple);
                if (parts.Length < 2) continue;
                HandleRelationParts(result, parts);
            }
        }

        return result;
    }

    protected string[] SplitTupleRespectingQuotes(string tuple)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        char quoteChar = '\0';
        for (int i = 0; i < tuple.Length; i++)
        {
            var c = tuple[i];
            if (!inQuotes && (c == '\'' || c == '"'))
            {
                inQuotes = true;
                quoteChar = c;
                current.Append(c);
                continue;
            }
            if (inQuotes && c == quoteChar)
            {
                inQuotes = false;
                current.Append(c);
                continue;
            }
            if (!inQuotes && c == ',')
            {
                parts.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) parts.Add(current.ToString().Trim());
        return parts.ToArray();
    }

    // Helper record describing a parsed tuple extracted from file text
    protected record ParsedTuple(long Id, string? Name, string? Guid, string[] Parts);

    // Extract tuples from arbitrary file text: yields only tuples whose first field is numeric (dump id)
    protected IEnumerable<ParsedTuple> EnumerateParsedTuplesFromText(string text)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        foreach (Match m in InsertValuesRegex.Matches(text))
        {
            var tuple = m.Groups[1].Value;
            var parts = SplitTupleRespectingQuotes(tuple);
            if (parts.Length == 0) continue;

            if (long.TryParse(parts[0], out var id))
            {
                string? name = parts.FirstOrDefault(p => p.StartsWith("'") && p.EndsWith("'"));
                if (name != null)
                {
                    name = name.Trim('\'', '"');
                }
                else
                {
                    var q = parts.FirstOrDefault(p => p.Contains("'") || p.Contains('"'));
                    if (q != null) name = q.Trim('\'', '"');
                }

                var gid = parts.FirstOrDefault(p => GuidRegex.IsMatch(p));
                gid = gid?.Trim('\'', '"');

                yield return new ParsedTuple(id, name, gid, parts);
            }
        }
    }

    // Helper: from a parts array returns ordered numeric pairs and optional relation type (if third part present)
    protected IEnumerable<(long A, long B, string? RelationType)> EnumerateNumericPairs(string[] parts)
    {
        var numericParts = parts.Select(p => { long v; return long.TryParse(p, out v) ? (long?)v : null; }).Where(x => x.HasValue).Select(x => x!.Value).ToList();
        if (numericParts.Count < 2) yield break;

        for (int i = 0; i < numericParts.Count; i++)
        {
            for (int j = 0; j < numericParts.Count; j++)
            {
                if (i == j) continue;
                string? rel = parts.Length >= 3 ? parts[2].Trim('\'', '"') : null;
                yield return (numericParts[i], numericParts[j], rel);
            }
        }
    }
}
