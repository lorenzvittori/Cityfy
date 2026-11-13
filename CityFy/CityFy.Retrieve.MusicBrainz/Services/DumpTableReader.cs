using System.Text;

namespace CityFy.Retrieve.MusicBrainz.Services;

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
