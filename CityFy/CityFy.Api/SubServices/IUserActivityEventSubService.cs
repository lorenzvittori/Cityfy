namespace CityFy.Api.SubServices;

public interface IUserActivityEventSubService
{
    /// <summary>UserActive: a CityFy page of the user is open (Retrieve.LastFm reads them at the fast cadence).</summary>
    Task PublishActiveAsync(string userId, CancellationToken ct);
}
