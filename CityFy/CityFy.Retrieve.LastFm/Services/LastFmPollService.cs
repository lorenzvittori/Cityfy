using CityFy.Retrieve.LastFm.Models;
using CityFy.Retrieve.LastFm.SubServices;
using ServiceDefault.Errors;

namespace CityFy.Retrieve.LastFm.Services;

public sealed class LastFmPollService(
    ILastFmAccountStoreSubService accounts,
    ILastFmScrobbleReadSubService scrobbles,
    INowPlayingStoreSubService nowPlaying,
    IUserActivitySubService activity,
    ILastFmPollCadenceSubService cadence,
    IListeningImportedEventSubService importedEvents,
    ILiveEventSubService liveEvents,
    ILastFmErrorSubService errors,
    ILogger<LastFmPollService> logger) : ILastFmPollService
{
    public async Task<int> PollDueAsync(CancellationToken ct)
    {
        var due = await accounts.ListDueAsync(DateTime.UtcNow, ct);
        foreach (var account in due)
        {
            try
            {
                await PollAsync(account, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                await HandleFailureAsync(account, ex, ct);
            }
        }
        return due.Count;
    }

    private async Task PollAsync(LastFmAccount account, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var sessionKey = accounts.GetSessionKey(account);
        var sourceRef = $"poll-{now:yyyyMMddHHmmss}";
        var result = await scrobbles.ReadNewAsync(account, sessionKey, sourceRef, ct);

        if (nowPlaying.Set(account.UserId, result.NowPlaying, now))
            await liveEvents.PublishNowPlayingAsync(account.UserId, nowPlaying.Get(account.UserId), ct);
        var isPlaying = result.NowPlaying is not null;
        var listeningAt = isPlaying ? now : result.NewestScrobbleAt;
        var lastListening = Latest(account.LastListeningAt, listeningAt);

        // Fast while something plays or the user has a CityFy page open (the bar shows the track)
        var fast = isPlaying || activity.IsActive(account.UserId, now);
        await accounts.UpdatePollAsync(account.UserId, now, result.NewestScrobbleAt ?? account.LastScrobbleAt, listeningAt,
            cadence.NextPollAt(now, fast, lastListening), ct);

        if (result.Inserted > 0)
        {
            await importedEvents.PublishAsync(account.UserId, sourceRef, result.Inserted, ct);
            logger.LogInformation("Last.fm {Username}: {Inserted} new plays", account.Username, result.Inserted);
        }
    }

    private async Task HandleFailureAsync(LastFmAccount account, Exception ex, CancellationToken ct)
    {
        var code = errors.ToErrorCode(ex, ErrorCodes.LastFmUnavailable);
        if (errors.IsAccountProblem(code))
        {
            // Revoked on Last.fm, or private: stop until the user links it again
            logger.LogWarning("Last.fm {Username} suspended ({Code}): {Message}", account.Username, code, ex.Message);
            await accounts.SuspendAsync(account.UserId, code, ct);
            if (nowPlaying.Forget(account.UserId))
                await liveEvents.PublishNowPlayingAsync(account.UserId, null, ct);
            return;
        }

        if (errors.IsConfigurationProblem(ex))
            logger.LogCritical(ex, "Last.fm refused the API key of CityFy: check LastFm:ApiKey");
        else
            logger.LogWarning("Last.fm {Username}: poll failed ({Code}) {Message}", account.Username, code, ex.Message);

        // Try again at the usual cadence (a rate-limit pause is held by the request gate anyway)
        var now = DateTime.UtcNow;
        await accounts.SetNextPollAsync(account.UserId, cadence.NextPollAt(now, activity.IsActive(account.UserId, now), account.LastListeningAt), ct);
    }

    private static DateTime? Latest(DateTime? a, DateTime? b) => a is null ? b : b is null ? a : a > b ? a : b;
}
