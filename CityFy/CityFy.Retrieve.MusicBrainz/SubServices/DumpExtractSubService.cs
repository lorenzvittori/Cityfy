using CityFy.Retrieve.MusicBrainz.Clients;
using CityFy.Retrieve.MusicBrainz.Services;

namespace CityFy.Retrieve.MusicBrainz.SubServices;

public sealed class DumpExtractSubService(IArchiveExtractor extractor, ImportProgress progress, ILogger<DumpExtractSubService> logger)
    : IDumpExtractSubService
{
    private const string ExtractedFolder = "extracted";
    private const string TablesFolder = "mbdump";

    public async Task<string> ExtractMissingAsync(string localDir, CancellationToken ct)
    {
        var extractDir = Path.Combine(localDir, ExtractedFolder);

        foreach (var (archive, tables) in DumpFiles.TablesByArchive)
        {
            var missing = tables.Where(t => IsMissing(extractDir, t)).ToList();
            if (missing.Count == 0) continue;

            logger.LogInformation("Extracting {Count} tables from {Archive}", missing.Count, archive);
            // tar gives no progress: indeterminate (the core archive takes several minutes)
            progress.Report("Extracting", null, $"{archive}: {string.Join(", ", missing.Select(Path.GetFileName))}");
            await extractor.ExtractAsync(Path.Combine(localDir, archive), extractDir, missing, ct);
        }

        return Path.Combine(extractDir, TablesFolder);
    }

    private static bool IsMissing(string extractDir, string table) => !File.Exists(Path.Combine(extractDir, table));
}