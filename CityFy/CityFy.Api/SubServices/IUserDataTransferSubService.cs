namespace CityFy.Api.SubServices;

/// <summary>Moves the data of one owner to another on every service (anonymous session → account).</summary>
public interface IUserDataTransferSubService
{
    /// <summary>
    /// Original data (uploads, plays) changes owner; the derived data of the old owner (pipeline runs,
    /// generated cities) is deleted, and the new owner's catalog is rebuilt when plays arrived.
    /// Every step runs even if an earlier one failed. Returns the services to retry (empty = done);
    /// repeating the whole transfer is safe.
    /// </summary>
    Task<IReadOnlyList<string>> TransferEverywhereAsync(string fromUserId, string toUserId, CancellationToken ct);
}
