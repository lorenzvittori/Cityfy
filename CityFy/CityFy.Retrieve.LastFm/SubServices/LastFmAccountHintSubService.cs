using CityFy.Retrieve.LastFm.Models;
using CityFy.Retrieve.LastFm.Options;
using Microsoft.Extensions.Options;
using ServiceDefault.Errors;

namespace CityFy.Retrieve.LastFm.SubServices;

public sealed class LastFmAccountHintSubService(IOptions<LastFmOptions> options) : ILastFmAccountHintSubService
{
    private readonly TimeSpan _quiet = TimeSpan.FromDays(options.Value.NotScrobblingDays);

    public string? WarningOf(LastFmAccount account, DateTime now)
    {
        if (account.Status != LastFmAccountStatus.Active)
            return null; // a suspended account already has its error
        var lastNews = account.LastScrobbleAt > account.LinkedAt ? account.LastScrobbleAt : account.LinkedAt;
        return now - lastNews > _quiet ? ErrorCodes.LastFmSpotifyNotScrobbling : null;
    }
}
