using CityFy.Retrieve.Spotify.Clients;
using CityFy.Retrieve.Spotify.Mappers;
using CityFy.Retrieve.Spotify.Models;
using CityFy.Retrieve.Spotify.Options;
using CityFy.Retrieve.Spotify.Repositories;
using Microsoft.Extensions.Options;
using ServiceDefault.Listening;

namespace CityFy.Retrieve.Spotify.SubServices;

public sealed class SpotifyPlaysRetrieveSubService(
    ISpotifyClient client,
    ISpotifyRepository repo,
    IListeningEventStore store,
    ISpotifyConverter converter,
    IOptions<SpotifyOptions> options,
    ILogger<SpotifyPlaysRetrieveSubService> logger) : ISpotifyPlaysRetrieveSubService
{
    // Safety net against a cursor that never ends; recently-played holds ~50 items anyway.
    private const int MaxPages = 20;

    private readonly int _pageSize = options.Value.PageSize;

    public Task<(int Inserted, DateTime? LastPlayedAt)> RetrieveAsync(RetrieveJob job, string accessToken, DateTime? since, CancellationToken ct) =>
        since is { } s ? RetrieveSinceAsync(job, accessToken, s, ct) : RetrieveAllAvailableAsync(job, accessToken, ct);

    // Incremental: only plays newer than the last imported one ('after' cursor).
    private async Task<(int Inserted, DateTime? LastPlayedAt)> RetrieveSinceAsync(RetrieveJob job, string token, DateTime since, CancellationToken ct)
    {
        var after = new DateTimeOffset(DateTime.SpecifyKind(since, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        await repo.UpdateProgressAsync(job.JobId, null, $"Plays after {since:yyyy-MM-dd HH:mm} UTC", 0);
        var page = await client.GetRecentlyPlayedPageAsync(token, before: null, after: after, _pageSize, ct);

        // A full page means plays may have been lost between two runs: Spotify only keeps the
        // most recent ~50. The fix is to run the retrieve more often (scheduler).
        if (page.Items.Count() >= _pageSize)
            logger.LogWarning("User {UserId}: {Count} plays since {Since}, some may be missing (retrieve more often)",
                job.UserId, page.Items.Count(), since);

        return await StoreAsync(job, page.Items, ct);
    }

    // First run (no cursor): walk back through everything Spotify still has.
    private async Task<(int Inserted, DateTime? LastPlayedAt)> RetrieveAllAvailableAsync(RetrieveJob job, string token, CancellationToken ct)
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

    private async Task<(int Inserted, DateTime? LastPlayedAt)> StoreAsync(RetrieveJob job, IEnumerable<SpotifyPlayHistoryDto> items, CancellationToken ct)
    {
        var events = converter.ToListeningEvents(items, job.UserId, job.JobId);
        var inserted = await store.UpsertAsync(events, ListeningSources.Api, ct);
        DateTime? last = events.Count > 0 ? events.Max(e => e.PlayedAt) : null;
        return (inserted, last);
    }
}