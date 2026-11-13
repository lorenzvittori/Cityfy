using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using CityFy.Api.Clients;
using CityFy.Api.Models;
using CityFy.Api.SubServices;
using ServiceDefault.Contracts;
using ServiceDefault.Errors;

namespace CityFy.Api.Services;

public sealed class LiveService(
    ILiveHubSubService hub,
    ILastFmServiceClient lastFm,
    IUserActivityEventSubService activity,
    ILogger<LiveService> logger) : ILiveService
{
    public const string NowPlayingEvent = "now-playing";
    public const string PingEvent = "ping";

    /// <summary>Below the usual idle timeouts of proxies (60 s and more).</summary>
    private static readonly TimeSpan PingEvery = TimeSpan.FromSeconds(25);

    /// <summary>Retrieve.LastFm keeps a user "active" for 150 s after a UserActive.</summary>
    private static readonly TimeSpan ActiveEvery = TimeSpan.FromSeconds(60);

    /// <summary>A change queued for longer (e.g. the gateway was down) is no longer "now".</summary>
    private static readonly TimeSpan MaxMessageAge = TimeSpan.FromMinutes(3);

    public async Task<LiveConnection> OpenAsync(string userId, CancellationToken ct)
    {
        var account = await lastFm.GetAccountAsync(userId, ct);
        var subscription = hub.TrySubscribe(userId)
            ?? throw new CityFyException(ErrorCodes.RateLimited,
                $"At most {LiveHubSubService.MaxConnectionsPerUser} live connections per user", StatusCodes.Status429TooManyRequests);
        await activity.PublishActiveAsync(userId, ct);
        return new LiveConnection(subscription, account.NowPlaying.ToDto());
    }

    public async IAsyncEnumerable<SseItem<NowPlayingDto?>> ReadAsync(string userId, LiveConnection connection, [EnumeratorCancellation] CancellationToken ct)
    {
        using var subscription = connection.Subscription;
        yield return new SseItem<NowPlayingDto?>(connection.Initial, NowPlayingEvent);

        var nextActive = DateTime.UtcNow + ActiveEvery;
        while (!ct.IsCancellationRequested)
        {
            NowPlayingDto? next;
            try
            {
                next = await WaitAsync(subscription, ct);
            }
            catch (OperationCanceledException)
            {
                yield break; // the page closed
            }

            if (next is null)
            {
                if (subscription.Events.Completion.IsCompleted)
                    yield break;
                yield return new SseItem<NowPlayingDto?>(null, PingEvent);
            }
            else
            {
                yield return new SseItem<NowPlayingDto?>(next, NowPlayingEvent);
            }

            if (DateTime.UtcNow >= nextActive)
            {
                nextActive = DateTime.UtcNow + ActiveEvery;
                await PublishActiveQuietlyAsync(userId, ct);
            }
        }
    }

    public Task OnNowPlayingChangedAsync(NowPlayingChanged message, CancellationToken ct)
    {
        if (DateTime.UtcNow - message.ChangedAt > MaxMessageAge)
            logger.LogDebug("NowPlayingChanged of {UserId} from {ChangedAt:o} is too old: dropped", message.UserId, message.ChangedAt);
        else
            hub.Publish(message.UserId, message.ToDto());
        return Task.CompletedTask;
    }

    /// <summary>The next change, or null after PingEvery without changes (or when the subscription ended).</summary>
    private static async Task<NowPlayingDto?> WaitAsync(LiveSubscription subscription, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(PingEvery);
        try
        {
            return await subscription.Events.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null; // ping time
        }
        catch (System.Threading.Channels.ChannelClosedException)
        {
            return null;
        }
    }

    /// <summary>A broker hiccup must not close the page: the next minute retries.</summary>
    private async Task PublishActiveQuietlyAsync(string userId, CancellationToken ct)
    {
        try
        {
            await activity.PublishActiveAsync(userId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "UserActive of {UserId} not published", userId);
        }
    }
}
