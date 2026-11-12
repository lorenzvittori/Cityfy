using CityFy.Retrieve.Spotify.Models;
using CityFy.Retrieve.Spotify.SubServices;
using ServiceDefault.Errors;
using ServiceDefault.Models;

namespace CityFy.Retrieve.Spotify.Services;

public sealed class SpotifyRetrieveService(
    ISpotifyAccountStoreSubService accounts,
    ISpotifyAccessTokenSubService accessTokens,
    IRetrieveJobSubService jobs,
    ISpotifyPlaysRetrieveSubService plays,
    IListeningImportedEventSubService importedEvents,
    IRetrieveErrorSubService errors,
    ILogger<SpotifyRetrieveService> logger) : ISpotifyRetrieveService
{
    public async Task<string> EnqueueRetrieveAsync(string userId, CancellationToken ct)
    {
        await accounts.EnsureLinkedAsync(userId, ct);
        var jobId = await jobs.CreateAndEnqueueAsync(userId, ct);
        logger.LogInformation("Spotify retrieve job {JobId} queued for user {UserId}", jobId, userId);
        return jobId;
    }

    public async Task<int> RunRetrieveAsync(RetrieveJob job, CancellationToken stoppingToken)
    {
        if (await jobs.TryStartAsync(job, stoppingToken) is not { } cts)
        {
            logger.LogInformation("Spotify retrieve job {JobId} was cancelled or deleted before starting", job.JobId);
            return 0;
        }

        var ct = cts.Token;
        try
        {
            var token = await accessTokens.GetAsync(job.UserId, ct);
            var since = (await accounts.GetAsync(job.UserId, ct))?.LastPlayedAt;
            var (inserted, lastPlayedAt) = await plays.RetrieveAsync(job, token, since, ct);
            await accounts.UpdateRetrieveCursorAsync(job.UserId, DateTime.UtcNow, lastPlayedAt, ct);

            // The orchestrator rebuilds the city only when something changed
            if (inserted > 0)
                await importedEvents.PublishAsync(job, inserted, ct);

            await jobs.CompleteAsync(job.JobId, inserted);
            logger.LogInformation("Spotify retrieve job {JobId} done for user {UserId}: {Inserted} new plays", job.JobId, job.UserId, inserted);
            return inserted;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            // Stopped by the admin (status already Cancelled); plays stored so far stay
            logger.LogInformation("Spotify retrieve job {JobId} cancelled by the admin", job.JobId);
            return 0;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await jobs.FailAsync(job.JobId, "Cancelled (service shutting down)", ErrorCodes.SpotifyRetrieveFailed);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Spotify retrieve job {JobId} failed", job.JobId);
            await jobs.FailAsync(job.JobId, ex.Message, errors.ToErrorCode(ex));
            throw;
        }
        finally
        {
            jobs.End(job.JobId, cts);
        }
    }

    public async Task CancelAsync(string jobId, CancellationToken ct)
    {
        var previous = await jobs.CancelAsync(jobId, ct);
        logger.LogInformation("Spotify retrieve job {JobId} cancelled ({Status})", jobId, previous);
    }

    public Task<ProcessingTask?> GetJobAsync(string jobId, CancellationToken ct) => jobs.GetAsync(jobId, ct);

    public Task<IReadOnlyList<ProcessingTask>> ListJobsAsync(int limit, CancellationToken ct) => jobs.ListAsync(limit, ct);
}
