using System.Text.RegularExpressions;

namespace CityFy.Retrieve.MusicBrainz.Services;

public interface IFileService
{
    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default);
    Task<IEnumerable<string[]>> ReadWhitespaceSplitLinesAsync(string path, CancellationToken cancellationToken = default);
}

public class FileService : IFileService
{
    private static readonly Regex WhiteSplit = new(@"\s+", RegexOptions.Compiled);

    public async Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
    {
        return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IEnumerable<string[]>> ReadWhitespaceSplitLinesAsync(string path, CancellationToken cancellationToken = default)
    {
        var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
        var result = new List<string[]>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            // Prefer tab split; fallback to any whitespace
            var parts = line.Split('\t');
            if (parts.Length == 1)
            {
                parts = WhiteSplit.Split(line).Where(p => !string.IsNullOrEmpty(p)).ToArray();
            }
            result.Add(parts.Select(p => p.Trim()).ToArray());
        }
        return result;
    }
}
