using System.Text;

namespace CityFy.Retrieve.MusicBrainz.Services;

/// <summary>
/// Archives and tables of the MusicBrainz full export used by the import.
/// Column layouts follow the MusicBrainz schema (CreateTables.sql), see the readers in
/// <see cref="MusicBrainzImporter"/>.
/// </summary>
public static class DumpFiles
{
    public const string CoreArchive = "mbdump.tar.bz2";
    public const string DerivedArchive = "mbdump-derived.tar.bz2";

    /// <summary>Tables to extract from each archive (tar member paths).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> TablesByArchive = new Dictionary<string, string[]>
    {
        [CoreArchive] = ["mbdump/genre", "mbdump/link_type", "mbdump/link", "mbdump/l_genre_genre",
                         "mbdump/artist", "mbdump/l_artist_url", "mbdump/url"],
        [DerivedArchive] = ["mbdump/tag", "mbdump/artist_tag"],
    };
}

/// <summary>
/// Streaming reader for the PostgreSQL COPY text format used by the dump: one row per line,
/// tab-separated columns, "\N" for NULL, backslash escapes inside values.
/// Files are read line by line (url is several GB).
/// </summary>
public static class DumpTableReader
{
    /// <param name="onProgress">Called every ~65k lines with the bytes read so far (approximate: buffered).</param>
    public static IEnumerable<string[]> ReadRows(string path, CancellationToken ct = default, Action<long>? onProgress = null)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1 << 16);
        string? line;
        long n = 0;
        while ((line = reader.ReadLine()) != null)
        {
            if ((++n & 0xFFFF) == 0)
            {
                ct.ThrowIfCancellationRequested();
                onProgress?.Invoke(reader.BaseStream.Position);
            }
            if (line.Length == 0) continue;
            yield return line.Split('\t');
        }
    }

    public static bool IsNull(string value) => value == @"\N";

    /// <summary>Decodes COPY escapes (\\, \t, \n, \r) of a text column.</summary>
    public static string Unescape(string value)
    {
        if (value.IndexOf('\\') < 0) return value;

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\\' && i + 1 < value.Length)
            {
                var next = value[++i];
                sb.Append(next switch { 't' => '\t', 'n' => '\n', 'r' => '\r', _ => next });
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
