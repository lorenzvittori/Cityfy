using CityFy.Api.Models;

namespace CityFy.Api.Services;

/// <summary>
/// End of the Last.fm licence (terms 9.3: "delete all Last.fm Data in Your possession"): deletes every
/// Last.fm account and, since an account cannot log in without Last.fm, all the data of every
/// account on every service. Anonymous sessions (export only) are not touched.
/// </summary>
public interface ILastFmPurgeService
{
    /// <summary>
    /// First Retrieve.LastFm (stops polling and imports), then every account user by user. Throws
    /// USER_DATA_DELETION_INCOMPLETE (502) when something could not be deleted: repeating it is safe.
    /// </summary>
    Task<LastFmPurgeResult> PurgeAllAsync(string requestedBy, CancellationToken ct);
}
