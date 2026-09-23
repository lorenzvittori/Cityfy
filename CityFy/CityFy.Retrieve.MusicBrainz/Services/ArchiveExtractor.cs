using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace CityFy.Retrieve.MusicBrainz.Services;

public class ArchiveExtractor
{
    private readonly ILogger<ArchiveExtractor> _logger;

    public ArchiveExtractor(ILogger<ArchiveExtractor> logger)
    {
        _logger = logger;
    }

    // Estrae un archivio .tar.bz2 in destinationDirectory usando il comando tar esterno.
    // Nota: richiede che il sistema abbia disponibile 'tar' (Windows 10+ include bsdtar),
    // altrimenti implementare l'estrazione con una libreria compatibile.
    public async Task ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("Archive not found", archivePath);

        Directory.CreateDirectory(destinationDirectory);

        var psi = new ProcessStartInfo
        {
            FileName = "tar",
            Arguments = $"-xjf \"{archivePath}\" -C \"{destinationDirectory}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc == null)
            throw new InvalidOperationException("Unable to start tar process for extraction");

        _logger.LogInformation("Started tar (pid={Pid}) to extract {Archive} -> {Dest}", proc.Id, archivePath, destinationDirectory);

        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        proc.Exited += (_, __) => tcs.TrySetResult(proc.ExitCode);
        proc.EnableRaisingEvents = true;

        var stdoutSb = new System.Text.StringBuilder();
        var stderrSb = new System.Text.StringBuilder();

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            try { stdoutSb.AppendLine(e.Data); _logger.LogInformation("tar: {Line}", e.Data); } catch { }
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            try { stderrSb.AppendLine(e.Data); _logger.LogWarning("tar stderr: {Line}", e.Data); } catch { }
        };

        // start asynchronous line reading
        try
        {
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to begin async read of tar output; falling back to synchronous read");
        }

        using (cancellationToken.Register(() => {
            try
            {
                _logger.LogInformation("Cancellation requested: killing tar process (pid={Pid})", proc.Id);
                if (!proc.HasExited) proc.Kill(true);
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Error killing tar process"); }
        }))
        {
            var exit = await tcs.Task;

            // small delay to allow final output events to be raised
            await Task.Delay(50);

            var stdout = stdoutSb.ToString();
            var stderr = stderrSb.ToString();

            if (exit != 0)
            {
                _logger.LogError("tar exited with code {Exit}. stderr (last lines): {Stderr}", exit, string.Join("\n", stderr.Split('\n').TakeLast(20)));
                throw new InvalidOperationException($"tar exited with code {exit}. stderr: {stderr}");
            }
            else
            {
                _logger.LogInformation("tar extraction completed successfully (pid={Pid}). stdout sample: {Sample}", proc.Id, string.Join("; ", stdout.Split('\n').Take(5)));
            }
        }
    }
}
