using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace CityFy.Retrieve.MusicBrainz.Clients;

public sealed class ArchiveExtractor(ILogger<ArchiveExtractor> logger) : IArchiveExtractor
{
    // Extracts a .tar.bz2 archive into destinationDirectory with the external 'tar' command.
    // Requires 'tar' on the system (Windows 10+ ships bsdtar); otherwise use a compatible library.
    // members: optional list of archive entries to extract (e.g. "mbdump/genre"); all when empty.
    private static string Quoted(string member) => $"\"{member}\"";

    public async Task ExtractAsync(string archivePath, string destinationDirectory, IReadOnlyCollection<string>? members = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("Archive not found", archivePath);

        Directory.CreateDirectory(destinationDirectory);

        var memberArgs = members is { Count: > 0 } ? " " + string.Join(" ", members.Select(Quoted)) : "";
        var psi = new ProcessStartInfo
        {
            FileName = "tar",
            Arguments = $"-xjf \"{archivePath}\" -C \"{destinationDirectory}\"{memberArgs}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc == null)
            throw new InvalidOperationException("Unable to start tar process for extraction");

        logger.LogInformation("Started tar (pid={Pid}) to extract {Archive} -> {Dest}", proc.Id, archivePath, destinationDirectory);

        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        proc.Exited += (_, __) => tcs.TrySetResult(proc.ExitCode);
        proc.EnableRaisingEvents = true;

        var stdoutSb = new System.Text.StringBuilder();
        var stderrSb = new System.Text.StringBuilder();

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            try { stdoutSb.AppendLine(e.Data); logger.LogInformation("tar: {Line}", e.Data); } catch { }
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            try { stderrSb.AppendLine(e.Data); logger.LogWarning("tar stderr: {Line}", e.Data); } catch { }
        };

        // start asynchronous line reading
        try
        {
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to begin async read of tar output; falling back to synchronous read");
        }

        using (cancellationToken.Register(() => {
            try
            {
                logger.LogInformation("Cancellation requested: killing tar process (pid={Pid})", proc.Id);
                if (!proc.HasExited) proc.Kill(true);
            }
            catch (Exception ex) { logger.LogDebug(ex, "Error killing tar process"); }
        }))
        {
            var exit = await tcs.Task;

            // small delay to allow final output events to be raised
            await Task.Delay(50);

            var stdout = stdoutSb.ToString();
            var stderr = stderrSb.ToString();

            if (exit != 0)
            {
                logger.LogError("tar exited with code {Exit}. stderr (last lines): {Stderr}", exit, string.Join("\n", stderr.Split('\n').TakeLast(20)));
                throw new InvalidOperationException($"tar exited with code {exit}. stderr: {stderr}");
            }
            else
            {
                logger.LogInformation("tar extraction completed successfully (pid={Pid}). stdout sample: {Sample}", proc.Id, string.Join("; ", stdout.Split('\n').Take(5)));
            }
        }
    }
}
