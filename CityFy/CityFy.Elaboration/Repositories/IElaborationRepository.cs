using CityFy.Elaboration.Models;
using ServiceDefault.City;

namespace CityFy.Elaboration.Repositories;

public interface IElaborationRepository
{
    /// <summary>Per-artist play totals of a user since a date (all history when null).</summary>
    Task<List<ArtistPlays>> AggregateArtistPlaysAsync(string userId, DateTime? since, int qualifiedPlayMs, CancellationToken ct);

    Task SaveCityAsync(CityModel city, CancellationToken ct);
    Task<CityModel?> GetCityAsync(string userId, string period, CancellationToken ct);

    /// <summary>Has the user any play at all (any period, any source)?</summary>
    Task<bool> HasPlaysAsync(string userId, CancellationToken ct);

    /// <summary>Data deletion: every play and every city of the user.</summary>
    Task<(long Plays, long Cities)> DeleteUserDataAsync(string userId, CancellationToken ct);
}
