using CityFy.Retrieve.MusicBrainz.Services;
using CityFy.Retrieve.MusicBrainz.Models;
using Microsoft.Extensions.Configuration;

namespace CityFy.Retrieve.MusicBrainz.Services;

public class RetrieveService
{
    private readonly MbDumpClient _mb;
    private readonly ArchiveExtractor _extractor;
    private readonly GenreParser _parser;
    private readonly DataImporter _importer;
    private readonly ILogger<RetrieveService> _logger;
    private readonly IConfiguration _configuration;

    public RetrieveService(MbDumpClient mb, ArchiveExtractor extractor, GenreParser parser, DataImporter importer, ILogger<RetrieveService> logger, IConfiguration configuration)
    {
        _mb = mb;
        _extractor = extractor;
        _parser = parser;
        _importer = importer;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<RetrieveResult> RunRetrieveAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting MusicBrainz retrieve job");
        var remotePath = await _mb.FindLatestDumpAsync(cancellationToken);
        if (remotePath == null)
            throw new InvalidOperationException("No mbdump file found on remote server");
        var remoteInfo = await _mb.GetRemoteFileInfoAsync(remotePath, cancellationToken);

        var remoteDir = Path.GetDirectoryName(remotePath)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(remoteDir))
            throw new InvalidOperationException("Invalid remote dump path returned: " + remotePath);

        var configuredRoot = _configuration["DumpStorage:RootPath"];
        var storageRoot = !string.IsNullOrEmpty(configuredRoot)
            ? configuredRoot
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cityfy", "mb_dumps");
        Directory.CreateDirectory(storageRoot);

        var localDir = Path.Combine(storageRoot, remoteDir);
        var localArchivePath = Path.Combine(localDir, "mbdump.tar.bz2");
        var remoteDirName = Path.GetFileName(remoteDir.TrimEnd('/'));
        var hasRemoteDate = DateTime.TryParseExact(
            remoteDirName,
            "yyyyMMdd-HHmmss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var remoteDate);

        DateTime? latestLocalDate = null;
        string? latestLocalDir = null;
        foreach (var d in Directory.EnumerateDirectories(storageRoot))
        {
            var name = Path.GetFileName(d);
            if (DateTime.TryParseExact(
                name,
                "yyyyMMdd-HHmmss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var localDate))
            {
                if (!latestLocalDate.HasValue || localDate > latestLocalDate.Value)
                {
                    latestLocalDate = localDate;
                    latestLocalDir = d;
                }
            }
        }

        var shouldDownload = true;
        if (hasRemoteDate && latestLocalDate.HasValue && latestLocalDir != null)
        {
            if (latestLocalDate.Value >= remoteDate)
            {
                localDir = latestLocalDir;
                localArchivePath = Path.Combine(localDir, "mbdump.tar.bz2");
                shouldDownload = false;
                _logger.LogInformation(
                    "Local dump {LocalDir} ({LocalDate}) is newer or equal to remote {RemoteDir} ({RemoteDate}); skipping download",
                    localDir,
                    latestLocalDate.Value,
                    remoteDirName,
                    remoteDate);
            }
            else
            {
                _logger.LogInformation(
                    "Remote dump {RemoteDir} ({RemoteDate}) is newer than local {LocalDate}; downloading new dump",
                    remoteDirName,
                    remoteDate,
                    latestLocalDate.Value);
            }
        }
        else if (!hasRemoteDate)
        {
            _logger.LogWarning(
                "Remote directory name {RemoteDir} is not in expected format yyyyMMdd-HHmmss; falling back to local file existence check",
                remoteDirName);
            if (File.Exists(localArchivePath))
            {
                shouldDownload = false;
                _logger.LogInformation("Local archive already up-to-date at {Path}; skipping download", localArchivePath);
            }
        }
        else if (File.Exists(localArchivePath))
        {
            shouldDownload = false;
            _logger.LogInformation("Local archive already up-to-date at {Path}; skipping download", localArchivePath);
        }

        if (!shouldDownload && File.Exists(localArchivePath) && remoteInfo?.ContentLength is long remoteSize)
        {
            var localSize = new FileInfo(localArchivePath).Length;
            if (localSize != remoteSize)
            {
                _logger.LogWarning(
                    "Local archive size {LocalSize} differs from remote size {RemoteSize}; re-downloading",
                    localSize,
                    remoteSize);
                try
                {
                    File.Delete(localArchivePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete local archive {Path}", localArchivePath);
                }

                shouldDownload = true;
            }
        }

        if (shouldDownload)
        {
            foreach (var d in Directory.EnumerateDirectories(storageRoot))
            {
                try
                {
                    if (!Path.GetFullPath(d).Equals(Path.GetFullPath(localDir), StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("Deleting old dump directory {Dir}", d);
                        Directory.Delete(d, true);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete old dump directory {Dir}", d);
                }
            }

            Directory.CreateDirectory(localDir);
            _logger.LogInformation("Downloading {RemotePath} to {LocalPath}", remotePath, localArchivePath);
            await _mb.DownloadAsync(remotePath, localArchivePath, null, cancellationToken);
        }

        var extractDir = Path.Combine(localDir, "extracted");
        if (!Directory.Exists(extractDir))
        {
            _logger.LogInformation("Extracting archive {Archive} to {Dir}", localArchivePath, extractDir);
            await _extractor.ExtractAsync(localArchivePath, extractDir, cancellationToken);
        }
        else
        {
            _logger.LogInformation("Extraction directory already exists {Dir}; skipping extraction", extractDir);
        }

        _logger.LogInformation("Parsing genres and relations (core files)");
        var parsed = await _parser.ParseCoreDumpAsync(extractDir, cancellationToken);

        _logger.LogInformation("Importing parsed data into database");
        await _importer.ImportAsync(parsed, cancellationToken);

        _logger.LogInformation("Retrieve job completed: {Genres} genres, {Relations} relations", parsed.Genres.Count, parsed.Relations.Count);
        return new RetrieveResult { Genres = parsed.Genres.Count, Relations = parsed.Relations.Count };
    }
}
