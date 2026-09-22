using System.Text.RegularExpressions;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace CityFy.Retrieve.MusicBrainz.Services;

public class MbDumpClient
{
    private readonly HttpClient _http;
    private readonly ILogger<MbDumpClient> _logger;
    private const string BaseUrl = "https://data.metabrainz.org/pub/musicbrainz/data/fullexport/";

    public MbDumpClient(HttpClient http, ILogger<MbDumpClient> logger)
    {
        _http = http;
        _logger = logger;
        if (_http.BaseAddress == null)
            _http.BaseAddress = new Uri(BaseUrl);
    }

    public class RemoteFileInfo
    {
        public string? ETag { get; set; }
        public DateTimeOffset? LastModified { get; set; }
        public long? ContentLength { get; set; }
    }

    // Ottiene headers utili (ETag, Last-Modified, Content-Length) tramite HEAD
    public async Task<RemoteFileInfo?> GetRemoteFileInfoAsync(string fileNameOrUrl, CancellationToken cancellationToken = default)
    {
        var requestUri = fileNameOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? fileNameOrUrl : new Uri(new Uri(BaseUrl), fileNameOrUrl).ToString();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, requestUri);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("HEAD request for {Url} returned status {Status}", requestUri, resp.StatusCode);
                return null;
            }

            var info = new RemoteFileInfo();
            if (resp.Headers.ETag != null)
                info.ETag = resp.Headers.ETag.ToString();
            if (resp.Content?.Headers?.LastModified != null)
                info.LastModified = resp.Content.Headers.LastModified;
            if (resp.Content?.Headers?.ContentLength != null)
                info.ContentLength = resp.Content.Headers.ContentLength;
            return info;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get remote file info for {Url}", fileNameOrUrl);
            return null;
        }
    }

    // Cerca la directory export più recente (yyyyMMdd-HHmmss) e poi mbdump.tar.bz2 al suo interno
    public async Task<string?> FindLatestDumpAsync(CancellationToken cancellationToken = default)
    {
        var html = await _http.GetStringAsync("", cancellationToken);
        var matches = Regex.Matches(html, "href=[\"'](?<dir>\\d{8}-\\d{6}/)[\"']", RegexOptions.IgnoreCase);
        DateTime? bestDate = null;
        string? bestDir = null;
        foreach (Match m in matches)
        {
            var dir = m.Groups["dir"].Value.TrimEnd('/');
            if (DateTime.TryParseExact(dir, "yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt))
            {
                if (!bestDate.HasValue || dt > bestDate.Value)
                {
                    bestDate = dt;
                    bestDir = dir;
                }
            }
        }

        if (bestDir == null)
            return null;

        var dirHtml = await _http.GetStringAsync(bestDir + "/", cancellationToken);
        var fileMatch = Regex.Match(dirHtml, "href=[\"'](?<file>mbdump\\.tar\\.bz2)[\"']", RegexOptions.IgnoreCase);
        if (!fileMatch.Success)
            return null;

        return bestDir + "/" + fileMatch.Groups["file"].Value;
    }

    // Scarica il file specificato dalla base URL in modo streaming, con report di progresso (percentuale)
    public async Task DownloadAsync(string fileNameOrUrl, string destinationPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var requestUri = fileNameOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? fileNameOrUrl : new Uri(new Uri(BaseUrl), fileNameOrUrl).ToString();
        using var resp = await _http.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1L;
        using var source = await resp.Content.ReadAsStreamAsync(cancellationToken);
        using var dest = File.Create(destinationPath);
        var buffer = new byte[81920];
        long totalRead = 0;
        int read;
        var sw = Stopwatch.StartNew();
        double lastReportedPercent = -1;
        var lastLog = DateTime.UtcNow;
        _logger.LogInformation("Starting download from {Url} to {Path}. Total bytes: {Total}", requestUri, destinationPath, total >= 0 ? total.ToString() : "unknown");
        while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            await dest.WriteAsync(buffer, 0, read, cancellationToken);
            totalRead += read;

            if (total > 0)
            {
                var percent = Math.Floor((double)totalRead / total * 100.0);
                if (percent != lastReportedPercent)
                {
                    lastReportedPercent = percent;
                    progress?.Report(percent);
                    // log at every percent change but not more than once per second
                    if ((DateTime.UtcNow - lastLog).TotalSeconds >= 1)
                    {
                        var speed = totalRead / Math.Max(1, sw.Elapsed.TotalSeconds);
                        _logger.LogInformation("Download progress: {Percent}% ({Read}/{Total} bytes) speed {Speed}/s", percent, totalRead, total, FormatBytes((long)speed));
                        lastLog = DateTime.UtcNow;
                    }
                }
            }
            else
            {
                // total unknown: log every 10 MB or every 5 seconds
                if (totalRead % (10 * 1024 * 1024) < buffer.Length || (DateTime.UtcNow - lastLog).TotalSeconds >= 5)
                {
                    var speed = totalRead / Math.Max(1, sw.Elapsed.TotalSeconds);
                    _logger.LogInformation("Download progress: {Read} bytes downloaded (speed {Speed}/s)", totalRead, FormatBytes((long)speed));
                    lastLog = DateTime.UtcNow;
                }
            }
        }
        sw.Stop();
        var finalSpeed = totalRead / Math.Max(1, sw.Elapsed.TotalSeconds);
        _logger.LogInformation("Download completed: {Bytes} bytes in {Seconds}s (avg {Speed}/s)", totalRead, sw.Elapsed.TotalSeconds, FormatBytes((long)finalSpeed));
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
        if (bytes < 1024 * 1024 * 1024) return (bytes / (1024.0 * 1024.0)).ToString("0.0") + " MB";
        return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("0.0") + " GB";
    }
}
