using CityFy.Catalog.Models;
using CityFy.Catalog.Options;
using CityFy.Catalog.Repositories;
using Microsoft.Extensions.Options;

namespace CityFy.Catalog.SubServices;

public sealed class ListeningAggregationSubService(ICatalogRepository repo, IOptions<CityOptions> options) : IListeningAggregationSubService
{
    public async Task<IReadOnlyList<ArtistPlays>> AggregateAsync(string userId, DateTime? since, DateTime? until, CancellationToken ct) =>
        await repo.AggregateArtistPlaysAsync(userId, since, until, options.Value.QualifiedPlayMs, ct);

    public Task<bool> HasPlaysAsync(string userId, CancellationToken ct) => repo.HasPlaysAsync(userId, ct);
}
