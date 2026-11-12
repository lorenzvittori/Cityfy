using CityFy.Elaboration.Models;
using CityFy.Elaboration.Options;
using CityFy.Elaboration.Repositories;
using Microsoft.Extensions.Options;

namespace CityFy.Elaboration.SubServices;

public sealed class ListeningAggregationSubService(IElaborationRepository repo, IOptions<CityOptions> options) : IListeningAggregationSubService
{
    public async Task<IReadOnlyList<ArtistPlays>> AggregateAsync(string userId, DateTime? since, CancellationToken ct) =>
        await repo.AggregateArtistPlaysAsync(userId, since, options.Value.QualifiedPlayMs, ct);

    public Task<bool> HasPlaysAsync(string userId, CancellationToken ct) => repo.HasPlaysAsync(userId, ct);
}
