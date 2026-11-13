using ServiceDefault.Models;

namespace CityFy.Catalog.SubServices;

public interface IUserDataTransferSubService
{
    /// <summary>
    /// The plays (all sources) of <paramref name="fromUserId"/> go to <paramref name="toUserId"/>; the
    /// artist catalog of the old owner is deleted (the new owner's is rebuilt by the pipeline). Idempotent.
    /// </summary>
    Task<UserDataTransfer> TransferAsync(string fromUserId, string toUserId, CancellationToken ct);
}
