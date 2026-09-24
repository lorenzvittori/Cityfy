namespace CityFy.Retrieve.MusicBrainz.Services;

public interface IDumpDownloadService
{
    // Ensure the latest dump is available locally. Returns (localDir, localArchivePath).
    Task<(string localDir, string localArchivePath)> GetOrDownloadLatestAsync(CancellationToken cancellationToken = default);
}

public class DumpDownloadService : IDumpDownloadService
{
    private readonly MbDumpClient _mb;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DumpDownloadService> _logger;

    public DumpDownloadService(MbDumpClient mb, IConfiguration configuration, ILogger<DumpDownloadService> logger)
    {
        _mb = mb;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<(string localDir, string localArchivePath)> GetOrDownloadLatestAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Checking remote for latest dump");
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

        return (localDir, localArchivePath);
    }
}
