using CityFy.Retrieve.LastFm.Clients;
using CityFy.Retrieve.LastFm.Mappers;
using CityFy.Retrieve.LastFm.Models;
using CityFy.Retrieve.LastFm.Options;
using Microsoft.Extensions.Options;
using ServiceDefault.Listening;

namespace CityFy.Retrieve.LastFm.SubServices;

public sealed class LastFmScrobbleReadSubService(
    ILastFmClient client,
    ILastFmListeningMapper mapper,
    IListeningEventStore store,
    IOptions<LastFmOptions> options) : ILastFmScrobbleReadSubService
{
    private readonly int _maxPollPages = options.Value.MaxPollPages;

    public async Task<LastFmHistoryPageResult> ReadHistoryPageAsync(LastFmAccount account, string sessionKey, LastFmHistoryImport import, CancellationToken ct)
    {
        var page = await client.GetRecentTracksAsync(account.Username, sessionKey, from: null, to: import.Before, import.NextPage, ct);
        var inserted = await StoreAsync(account.UserId, page.Scrobbles, import.ImportId, ct);
        return new LastFmHistoryPageResult(inserted, page.TotalPages, page.Total);
    }

    public async Task<LastFmPollResult> ReadNewAsync(LastFmAccount account, string sessionKey, string sourceRef, CancellationToken ct)
    {
        var cursor = account.LastScrobbleAt;
        var first = await client.GetRecentTracksAsync(account.Username, sessionKey, from: cursor, to: null, page: 1, ct);
        var news = first.Scrobbles.Where(s => s.IsAfter(cursor)).ToList();

        if (first.TotalPages <= 1 || news.Count == 0)
        {
            var inserted = await StoreAsync(account.UserId, news, sourceRef, ct);
            return new LastFmPollResult(inserted, NewestOf(news), first.NowPlaying);
        }

        // Backlog of several pages. Fixed upper bound (the newest scrobble of page 1, included) so the
        // pages do not shift while they are read; oldest pages first, at most MaxPollPages.
        var top = news.Max(LastFmScrobble.ScrobbledAtOf);
        var to = top.AddSeconds(1);
        var lastPage = first.TotalPages;
        var firstPage = Math.Max(1, lastPage - _maxPollPages + 1);

        var read = new List<LastFmScrobble>();
        for (var p = lastPage; p >= firstPage; p--)
        {
            var page = p == 1 ? first : await client.GetRecentTracksAsync(account.Username, sessionKey, cursor, to, p, ct);
            read.AddRange(page.Scrobbles.Where(s => s.IsBetween(cursor, to)));
        }

        var stored = await StoreAsync(account.UserId, read, sourceRef, ct);
        // Everything up to the newest scrobble read is in: the next poll continues from there
        DateTime? newest = firstPage == 1 ? top : NewestOf(read);
        return new LastFmPollResult(stored, newest, first.NowPlaying);
    }

    private static DateTime? NewestOf(IReadOnlyList<LastFmScrobble> scrobbles) =>
        scrobbles.Count > 0 ? scrobbles.Max(LastFmScrobble.ScrobbledAtOf) : null;

    private Task<int> StoreAsync(string userId, IEnumerable<LastFmScrobble> scrobbles, string sourceRef, CancellationToken ct) =>
        store.UpsertAsync(mapper.ToListeningEvents(scrobbles, userId, sourceRef), ListeningSources.LastFm, ct);
}
