using CityFy.Retrieve.Spotify.Clients;
using CityFy.Retrieve.Spotify.Mappers;
using CityFy.Retrieve.Spotify.Models;
using CityFy.Retrieve.Spotify.Options;
using CityFy.Retrieve.Spotify.Repositories;
using Microsoft.Extensions.Options;
using ServiceDefault.Contracts;
using ServiceDefault.Errors;
using ServiceDefault.Hosting;
using ServiceDefault.Listening;
using ServiceDefault.Messaging;
using ServiceDefault.Models;

namespace CityFy.Retrieve.Spotify.Services;

public sealed class SpotifyRetrieveService(
    ISpotifyClient client,
    ISpotifyAccountService accounts,
    ISpotifyAccountRepository accountRepo,
    ISpotifyRepository repo,
    IListeningEventStore store,
    ISpotifyConverter converter,
    IRetrieveJobQueue queue,
    IMessagePublisher publisher,
    CancellationRegistry running,
    IOptions<SpotifyOptions> options,
    ILogger<SpotifyRetrieveService> logger) : ISpotifyRetrieveService
{
    // Safety net against a cursor that never ends; recently-played holds ~50 items anyway.
    private const int MaxPages = 20;

    private readonly IRetrieveJobQueue _queue = queue;
    private readonly CancellationRegistry _running = running;
    private readonly int _pageSize = options.Value.PageSize;

    public async Task<string> EnqueueRetrieveAsync(string userId, CancellationToken ct)
    {
        if (await accounts.GetAsync(userId, ct) == null)
            throw SpotifyAccountException.NotLinked(userId);

        var jobId = Guid.NewGuid().ToString("N");
        await repo.CreateTaskAsync(jobId, userId, ct);
        await _queue.EnqueueAsync(new RetrieveJob(jobId, userId), ct);
        logger.LogInformation("Spotify retrieve job {JobId} queued for user {UserId}", jobId, userId);
        return jobId;
    }

    public async Task<int> RunRetrieveAsync(RetrieveJob job, CancellationToken stoppingToken)
    {
        // Cancelled while still in the queue, or deleted with the user's data
        if ((await repo.GetTaskAsync(job.JobId, stoppingToken)) is not { } queued || queued.Status == ProcessingStatus.Cancelled)
        {
            logger.LogInformation("Spotify retrieve job {JobId} was cancelled or deleted before starting", job.JobId);
            return 0;
        }

        var cts = _running.Register(job.JobId, stoppingToken);
        var ct = cts.Token;
        await repo.UpdateTaskAsync(job.JobId, ProcessingStatus.InProgress);

        try
        {
            var token = await accounts.GetAccessTokenAsync(job.UserId, ct);
            var account = await accountRepo.GetAsync(job.UserId, ct);

            var (inserted, lastPlayedAt) = account?.LastPlayedAt is { } since
                ? await RetrieveSinceAsync(job, token, since, ct)
                : await RetrieveAllAvailableAsync(job, token, ct);

            await accountRepo.UpdateRetrieveCursorAsync(job.UserId, DateTime.UtcNow, lastPlayedAt, ct);

            // The orchestrator rebuilds the city only when something changed
            if (inserted > 0)
                await publisher.PublishAsync(
                    new ListeningImported(job.UserId, ListeningSources.Api, job.JobId, inserted, DateTime.UtcNow),
                    correlationId: job.JobId, ct);

            await repo.UpdateTaskAsync(job.JobId, ProcessingStatus.Done, inserted);

            logger.LogInformation("Spotify retrieve job {JobId} done for user {UserId}: {Inserted} new plays",
                job.JobId, job.UserId, inserted);
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
            await repo.UpdateTaskAsync(job.JobId, ProcessingStatus.Failed, message: "Cancelled (service shutting down)",
                errorCode: ErrorCodes.SpotifyRetrieveFailed);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Spotify retrieve job {JobId} failed", job.JobId);
            await repo.UpdateTaskAsync(job.JobId, ProcessingStatus.Failed, message: ex.Message, errorCode: ToErrorCode(ex));
            throw;
        }
        finally
        {
            _running.Unregister(job.JobId, cts);
        }
    }

    public async Task CancelAsync(string jobId, CancellationToken ct)
    {
        var task = await repo.GetTaskAsync(jobId, ct)
            ?? throw new CityFyException(ErrorCodes.RetrieveJobNotFound, $"Retrieve job '{jobId}' not found", StatusCodes.Status404NotFound);

        if (task.Status is not (ProcessingStatus.Pending or ProcessingStatus.InProgress))
            throw new CityFyException(ErrorCodes.ProcessNotCancellable, $"Retrieve job '{jobId}' is {task.Status}", StatusCodes.Status409Conflict);

        // Status first (a queued job will be skipped), then stop the running one if any
        await repo.UpdateTaskAsync(jobId, ProcessingStatus.Cancelled, message: "Cancelled by an administrator");
        _running.Cancel(jobId);
        logger.LogInformation("Spotify retrieve job {JobId} cancelled ({Status})", jobId, task.Status);
    }

    /// <summary>Error code shown to the user for a failed retrieve.</summary>
    internal static string ToErrorCode(Exception ex) => ex switch
    {
        CityFyException c => c.Code,
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } => ErrorCodes.SpotifyRateLimited,
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden } => ErrorCodes.SpotifyReauthRequired,
        HttpRequestException { StatusCode: null or >= System.Net.HttpStatusCode.InternalServerError } => ErrorCodes.SpotifyUnavailable,
        TaskCanceledException => ErrorCodes.SpotifyUnavailable, // HTTP timeout
        _ => ErrorCodes.SpotifyRetrieveFailed
    };

    public Task<ProcessingTask?> GetJobAsync(string jobId, CancellationToken ct) => repo.GetTaskAsync(jobId, ct);

    public Task<IReadOnlyList<ProcessingTask>> ListJobsAsync(int limit, CancellationToken ct) => repo.ListTasksAsync(limit, ct);

    // Incremental: only plays newer than the last imported one ('after' cursor).
    private async Task<(int Inserted, DateTime? LastPlayedAt)> RetrieveSinceAsync(
        RetrieveJob job, string token, DateTime since, CancellationToken ct)
    {
        var after = new DateTimeOffset(DateTime.SpecifyKind(since, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        await repo.UpdateProgressAsync(job.JobId, null, $"Plays after {since:yyyy-MM-dd HH:mm} UTC", 0);
        var page = await client.GetRecentlyPlayedPageAsync(token, before: null, after: after, _pageSize, ct);

        // A full page means plays may have been lost between two runs: Spotify only keeps the
        // most recent ~50. The fix is to run the retrieve more often (scheduler, F2).
        if (page.Items.Count() >= _pageSize)
            logger.LogWarning("User {UserId}: {Count} plays since {Since}, some may be missing (retrieve more often)",
                job.UserId, page.Items.Count(), since);

        return await StoreAsync(job, page.Items, ct);
    }

    // First run (no cursor): walk back through everything Spotify still has.
    private async Task<(int Inserted, DateTime? LastPlayedAt)> RetrieveAllAvailableAsync(
        RetrieveJob job, string token, CancellationToken ct)
    {
        var inserted = 0;
        DateTime? lastPlayedAt = null;
        long? before = null;

        for (var i = 0; i < MaxPages; i++)
        {
            // Number of pages unknown in advance: indeterminate progress, page counter as detail
            await repo.UpdateProgressAsync(job.JobId, null, $"Page {i + 1} · {inserted} new plays", inserted);
            var page = await client.GetRecentlyPlayedPageAsync(token, before, after: null, _pageSize, ct);
            var (pageInserted, pageLast) = await StoreAsync(job, page.Items, ct);
            inserted += pageInserted;
            if (pageLast.HasValue && (lastPlayedAt is null || pageLast > lastPlayedAt))
                lastPlayedAt = pageLast;

            // Stop when there is no next cursor, the page is empty, or the cursor does not move.
            if (!page.Items.Any() || page.Before is null || page.Before == before)
                break;

            before = page.Before;
        }

        return (inserted, lastPlayedAt);
    }

    private async Task<(int Inserted, DateTime? LastPlayedAt)> StoreAsync(
        RetrieveJob job, IEnumerable<SpotifyPlayHistoryDto> items, CancellationToken ct)
    {
        var events = converter.ToListeningEvents(items, job.UserId, job.JobId);
        var inserted = await store.UpsertAsync(events, ListeningSources.Api, ct);
        DateTime? last = events.Count > 0 ? events.Max(e => e.PlayedAt) : null;
        return (inserted, last);
    }
}
