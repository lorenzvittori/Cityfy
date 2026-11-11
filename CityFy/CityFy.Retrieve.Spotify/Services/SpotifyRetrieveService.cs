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

namespace CityFy.Retrieve.Spotify.Services
{
    public class SpotifyRetrieveService : ISpotifyRetrieveService
    {
        // Safety net against a cursor that never ends; recently-played holds ~50 items anyway.
        private const int MaxPages = 20;

        private readonly ISpotifyClient _client;
        private readonly ISpotifyAccountService _accounts;
        private readonly ISpotifyAccountRepository _accountRepo;
        private readonly ISpotifyRepository _repo;
        private readonly IListeningEventStore _store;
        private readonly ISpotifyConverter _converter;
        private readonly IRetrieveJobQueue _queue;
        private readonly IMessagePublisher _publisher;
        private readonly CancellationRegistry _running;
        private readonly ILogger<SpotifyRetrieveService> _logger;
        private readonly int _pageSize;

        public SpotifyRetrieveService(
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
            ILogger<SpotifyRetrieveService> logger)
        {
            _publisher = publisher;
            _running = running;
            _client = client;
            _accounts = accounts;
            _accountRepo = accountRepo;
            _repo = repo;
            _store = store;
            _converter = converter;
            _queue = queue;
            _logger = logger;
            _pageSize = options.Value.PageSize;
        }

        public async Task<string> EnqueueRetrieveAsync(string userId, CancellationToken ct)
        {
            if (await _accounts.GetAsync(userId, ct) == null)
                throw SpotifyAccountException.NotLinked(userId);

            var jobId = Guid.NewGuid().ToString("N");
            await _repo.CreateTaskAsync(jobId, userId, ct);
            await _queue.EnqueueAsync(new RetrieveJob(jobId, userId), ct);
            _logger.LogInformation("Spotify retrieve job {JobId} queued for user {UserId}", jobId, userId);
            return jobId;
        }

        public async Task<int> RunRetrieveAsync(RetrieveJob job, CancellationToken stoppingToken)
        {
            // Cancelled while still in the queue, or deleted with the user's data
            if ((await _repo.GetTaskAsync(job.JobId, stoppingToken)) is not { } queued || queued.Status == ProcessingStatus.CANCELLED)
            {
                _logger.LogInformation("Spotify retrieve job {JobId} was cancelled or deleted before starting", job.JobId);
                return 0;
            }

            var cts = _running.Register(job.JobId, stoppingToken);
            var ct = cts.Token;
            await _repo.UpdateTaskAsync(job.JobId, ProcessingStatus.WORKING_IN_PROGRESS);

            try
            {
                var token = await _accounts.GetAccessTokenAsync(job.UserId, ct);
                var account = await _accountRepo.GetAsync(job.UserId, ct);

                var (inserted, lastPlayedAt) = account?.LastPlayedAt is { } since
                    ? await RetrieveSinceAsync(job, token, since, ct)
                    : await RetrieveAllAvailableAsync(job, token, ct);

                await _accountRepo.UpdateRetrieveCursorAsync(job.UserId, DateTime.UtcNow, lastPlayedAt, ct);

                // The orchestrator rebuilds the city only when something changed
                if (inserted > 0)
                    await _publisher.PublishAsync(
                        new ListeningImported(job.UserId, ListeningSources.Api, job.JobId, inserted, DateTime.UtcNow),
                        correlationId: job.JobId, ct);

                await _repo.UpdateTaskAsync(job.JobId, ProcessingStatus.DONE, inserted);

                _logger.LogInformation("Spotify retrieve job {JobId} done for user {UserId}: {Inserted} new plays",
                    job.JobId, job.UserId, inserted);
                return inserted;
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
            {
                // Stopped by the admin (status already CANCELLED); plays stored so far stay
                _logger.LogInformation("Spotify retrieve job {JobId} cancelled by the admin", job.JobId);
                return 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                await _repo.UpdateTaskAsync(job.JobId, ProcessingStatus.FAILED, message: "Cancelled (service shutting down)",
                    errorCode: ErrorCodes.SpotifyRetrieveFailed);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Spotify retrieve job {JobId} failed", job.JobId);
                await _repo.UpdateTaskAsync(job.JobId, ProcessingStatus.FAILED, message: ex.Message, errorCode: ToErrorCode(ex));
                throw;
            }
            finally
            {
                _running.Unregister(job.JobId, cts);
            }
        }

        public async Task CancelAsync(string jobId, CancellationToken ct)
        {
            var task = await _repo.GetTaskAsync(jobId, ct)
                ?? throw new CityFyException(ErrorCodes.RetrieveJobNotFound, $"Retrieve job '{jobId}' not found", StatusCodes.Status404NotFound);

            if (task.Status is not (ProcessingStatus.Pending or ProcessingStatus.WORKING_IN_PROGRESS))
                throw new CityFyException(ErrorCodes.ProcessNotCancellable, $"Retrieve job '{jobId}' is {task.Status}", StatusCodes.Status409Conflict);

            // Status first (a queued job will be skipped), then stop the running one if any
            await _repo.UpdateTaskAsync(jobId, ProcessingStatus.CANCELLED, message: "Annullato dall'amministratore");
            _running.Cancel(jobId);
            _logger.LogInformation("Spotify retrieve job {JobId} cancelled ({Status})", jobId, task.Status);
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

        public Task<ProcessingTask?> GetJobAsync(string jobId, CancellationToken ct) => _repo.GetTaskAsync(jobId, ct);

        // Incremental: only plays newer than the last imported one ('after' cursor).
        private async Task<(int Inserted, DateTime? LastPlayedAt)> RetrieveSinceAsync(
            RetrieveJob job, string token, DateTime since, CancellationToken ct)
        {
            var after = new DateTimeOffset(DateTime.SpecifyKind(since, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
            await _repo.UpdateProgressAsync(job.JobId, null, $"Ascolti successivi al {since:dd/MM HH:mm}", 0);
            var page = await _client.GetRecentlyPlayedPageAsync(token, before: null, after: after, _pageSize, ct);

            // A full page means plays may have been lost between two runs: Spotify only keeps the
            // most recent ~50. The fix is to run the retrieve more often (scheduler, F2).
            if (page.Items.Count() >= _pageSize)
                _logger.LogWarning("User {UserId}: {Count} plays since {Since}, some may be missing (retrieve more often)",
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
                await _repo.UpdateProgressAsync(job.JobId, null, $"Pagina {i + 1} · {inserted} nuovi ascolti", inserted);
                var page = await _client.GetRecentlyPlayedPageAsync(token, before, after: null, _pageSize, ct);
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
            var events = _converter.ToListeningEvents(items, job.UserId, job.JobId);
            var inserted = await _store.UpsertAsync(events, ListeningSources.Api, ct);
            DateTime? last = events.Count > 0 ? events.Max(e => e.PlayedAt) : null;
            return (inserted, last);
        }
    }
}
