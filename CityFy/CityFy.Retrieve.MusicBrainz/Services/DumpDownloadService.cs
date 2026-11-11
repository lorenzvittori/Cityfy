namespace CityFy.Retrieve.MusicBrainz.Services;

public interface IDumpDownloadService
{
    /// <summary>
    /// Ensures the archives of the latest complete export are available locally.
    /// Returns the local directory of the export (one sub-directory per export, named like the
    /// remote one: yyyyMMdd-HHmmss).
    /// </summary>
    Task<string> GetOrDownloadLatestAsync(IReadOnlyCollection<string> archives, CancellationToken cancellationToken = default);
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

    public async Task<string> GetOrDownloadLatestAsync(IReadOnlyCollection<string> archives, CancellationToken cancellationToken = default)
    {
        var storageRoot = _configuration["DumpStorage:RootPath"];
        if (string.IsNullOrEmpty(storageRoot))
            storageRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cityfy", "mb_dumps");
        Directory.CreateDirectory(storageRoot);

        var remoteDir = await _mb.FindLatestDumpDirectoryAsync(archives, cancellationToken)
            ?? throw new InvalidOperationException("No complete MusicBrainz export found on the remote server");

        var localDir = Path.Combine(storageRoot, remoteDir);
        Directory.CreateDirectory(localDir);

        foreach (var archive in archives)
        {
            var localPath = Path.Combine(localDir, archive);
            var remotePath = $"{remoteDir}/{archive}";
            var remoteInfo = await _mb.GetRemoteFileInfoAsync(remotePath, cancellationToken);

            if (File.Exists(localPath) &&
                (remoteInfo?.ContentLength is not long size || new FileInfo(localPath).Length == size))
            {
                _logger.LogInformation("Archive {Archive} already available at {Path}", archive, localPath);
                continue;
            }

            // Download to a temporary name: an interrupted download is never mistaken for a complete one
            var partPath = localPath + ".part";
            _logger.LogInformation("Downloading {Remote} to {Local}", remotePath, localPath);
            await _mb.DownloadAsync(remotePath, partPath, null, cancellationToken);
            File.Move(partPath, localPath, overwrite: true);
        }

        DeleteOlderExports(storageRoot, localDir);
        return localDir;
    }

    private void DeleteOlderExports(string storageRoot, string keepDir)
    {
        foreach (var d in Directory.EnumerateDirectories(storageRoot))
        {
            if (Path.GetFullPath(d).Equals(Path.GetFullPath(keepDir), StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                _logger.LogInformation("Deleting old dump directory {Dir}", d);
                Directory.Delete(d, true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete old dump directory {Dir}", d);
            }
        }
    }
}
