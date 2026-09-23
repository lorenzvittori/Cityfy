using CityFy.Retrieve.MusicBrainz.Models;

namespace CityFy.Retrieve.MusicBrainz.Services;

public class RetrieveService
{
    private readonly IDumpDownloadService _downloader;
    private readonly ArchiveExtractor _extractor;
    private readonly GenreParser _parser;
    private readonly ArtistParser _artistParser;
    private readonly DataImporter _importer;
    private readonly ILogger<RetrieveService> _logger;
    private readonly IConfiguration _configuration;

    public RetrieveService(IDumpDownloadService downloader, ArchiveExtractor extractor, GenreParser parser, ArtistParser artistParser, DataImporter importer, ILogger<RetrieveService> logger, IConfiguration configuration)
    {
        _downloader = downloader;
        _extractor = extractor;
        _parser = parser;
        _artistParser = artistParser;
        _importer = importer;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<RetrieveResult> RunRetrieveAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting MusicBrainz retrieve job");

        var (localDir, localArchivePath) = await DownloadDumpAsync(cancellationToken);

        var extractDir = Path.Combine(localDir, "extracted");
        await ExtractDumpAsync(localArchivePath, extractDir, cancellationToken);

        var result = await ParseAndImportAsync(extractDir, cancellationToken);
        return result;
    }

    private async Task<(string localDir, string localArchivePath)> DownloadDumpAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Ensuring latest dump is available locally");
        var (localDir, localArchivePath) = await _downloader.GetOrDownloadLatestAsync(cancellationToken);
        _logger.LogInformation("Local dump ready at {LocalArchive}", localArchivePath);
        return (localDir, localArchivePath);
    }

    private async Task ExtractDumpAsync(string localArchivePath, string extractDir, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(extractDir))
        {
            _logger.LogInformation("Extracting archive {Archive} to {Dir}", localArchivePath, extractDir);
            await _extractor.ExtractAsync(localArchivePath, extractDir, cancellationToken);
        }
        else
        {
            _logger.LogInformation("Extraction directory already exists {Dir}; skipping extraction", extractDir);
        }
    }

    private async Task<RetrieveResult> ParseAndImportAsync(string extractDir, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Parsing genres and relations (core files)");
        var parsed = await _parser.ParseCoreDumpAsync(extractDir, cancellationToken);

        _logger.LogInformation("Parsing artists");
        var artistsParsed = await _artistParser.ParseCoreDumpAsync(extractDir, parsed, cancellationToken);

        // Log artist parse summary and a few examples for diagnostics
        try
        {
            var artistCount = artistsParsed?.Artists?.Count ?? 0;
            var sample = artistsParsed?.Artists?.Take(5).Select(a => $"{a.Id}:{a.Name}");
            _logger.LogInformation("Artists parsed: {Count}. Examples: {Examples}", artistCount, sample != null ? string.Join(", ", sample) : "none");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log artist parse examples");
        }

        _logger.LogInformation("Importing parsed data into database");
        await _importer.ImportAsync(parsed, artistsParsed, cancellationToken);

        _logger.LogInformation("Retrieve job completed: {Genres} genres, {Relations} relations", parsed.Genres.Count, parsed.Relations.Count);
        return new RetrieveResult { Genres = parsed.Genres.Count, Relations = parsed.Relations.Count };
    }
}
