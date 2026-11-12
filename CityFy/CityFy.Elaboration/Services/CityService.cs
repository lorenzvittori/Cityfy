using CityFy.Elaboration.SubServices;
using ServiceDefault.City;
using ServiceDefault.Contracts;

namespace CityFy.Elaboration.Services;

public sealed class CityService(
    IPeriodSubService periods,
    IListeningAggregationSubService listening,
    IArtistResolveSubService artists,
    IGenreHierarchySubService genres,
    ICityBuildSubService cityBuild,
    ICityStoreSubService cityStore,
    ICityBuildEventsSubService events,
    ICityBuildErrorSubService errors,
    ILogger<CityService> logger) : ICityService
{
    public async Task<CityModel> BuildAsync(string userId, string? period, CancellationToken ct)
    {
        var p = periods.Normalize(period);
        var now = DateTime.UtcNow;
        var from = periods.GetStart(p, now);

        var plays = await listening.AggregateAsync(userId, from, ct);
        var resolved = await artists.ResolveAsync(plays, ct);
        var hierarchy = await genres.GetAsync(ct);
        var city = cityBuild.Build(userId, p, from, now, plays, resolved, hierarchy);

        // A user without any play (e.g. data deleted while a build was queued) gets no city: storing
        // one would bring back data the user asked to delete. Checked right before saving.
        if (plays.Count == 0 && !await listening.HasPlaysAsync(userId, ct))
        {
            logger.LogInformation("City {CityId} not stored: user {UserId} has no plays", city.Id, userId);
            return city;
        }

        await cityStore.SaveAsync(city, ct);

        logger.LogInformation(
            "City {CityId}: {Districts} districts, {Placed}/{Artists} artists placed, coverage {Coverage}% of qualified plays",
            city.Id, city.Districts.Count, city.Summary.PlacedArtists, city.Summary.TotalArtists, city.Summary.CoveragePercent);
        return city;
    }

    public Task<CityModel?> GetAsync(string userId, string? period, CancellationToken ct) =>
        cityStore.GetAsync(userId, periods.Normalize(period), ct);

    public async Task HandleBuildRequestedAsync(BuildCityRequested message, CancellationToken ct)
    {
        var built = new List<string>();
        foreach (var raw in message.Periods)
        {
            if (!periods.TryNormalize(raw, out var period))
            {
                logger.LogWarning("Run {RunId}: invalid period '{Period}' skipped", message.RunId, raw);
                continue;
            }

            await BuildAsync(message.UserId, period, ct);
            built.Add(period);
        }

        await events.PublishBuiltAsync(message.RunId, message.UserId, built, ct);
    }

    public Task HandleBuildFailedAsync(BuildCityRequested message, Exception error, CancellationToken ct) =>
        events.PublishFailedAsync(message.RunId, message.UserId, errors.ToErrorCode(error), error.Message, ct);
}
