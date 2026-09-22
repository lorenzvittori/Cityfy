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

        var tcs = new TaskCompletionSource<int>();
        proc.Exited += (_, __) => tcs.TrySetResult(proc.ExitCode);
        proc.EnableRaisingEvents = true;

        // start reading streams
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        using (cancellationToken.Register(() => {
            try { if (!proc.HasExited) proc.Kill(true); } catch { }
        }))
        {
            var exit = await tcs.Task;
            // ensure we have consumed output
            await Task.WhenAll(stdoutTask, stderrTask);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (exit != 0)
            {
                _logger.LogError("tar exited with code {Exit}. stderr: {Stderr}", exit, stderr);
                throw new InvalidOperationException($"tar exited with code {exit}. stderr: {stderr}");
            }
            else
            {
                _logger.LogInformation("tar extraction completed successfully. stdout: {Stdout}", stdout);
            }
        }
    }
}
