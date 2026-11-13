namespace ServiceDefault.Listening;

/// <summary>Moves the plays of one owner to another (e.g. anonymous session → account).</summary>
public interface IListeningEventTransfer
{
    /// <summary>
    /// Every play of <paramref name="fromUserId"/> becomes a play of <paramref name="toUserId"/>. The id
    /// contains the owner, so each play is written again under the new id and the old one deleted; a
    /// play the target already has (same id, or the same play from another source) is merged: its
    /// sources added, the known fields filled. Idempotent: a
    /// transfer interrupted half-way can simply be repeated. Returns the plays moved.
    /// </summary>
    Task<long> TransferAsync(string fromUserId, string toUserId, CancellationToken ct);
}
