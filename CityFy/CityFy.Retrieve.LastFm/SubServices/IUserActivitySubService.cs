namespace CityFy.Retrieve.LastFm.SubServices;

/// <summary>
/// Users with a CityFy page open (UserActive every minute from the gateway), in memory: while active
/// they are read at the fast cadence. Lost on restart: the next UserActive restores it.
/// </summary>
public interface IUserActivitySubService
{
    void MarkActive(string userId, DateTime now);

    /// <summary>A UserActive arrived within ActiveWindowSeconds.</summary>
    bool IsActive(string userId, DateTime now);

    void Forget(string userId);

    void ForgetAll();
}
