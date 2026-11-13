namespace ServiceDefault.Listening;

public interface IListeningEventStore
{
    /// <summary>
    /// Idempotent upsert keyed by <see cref="ListeningEvent.Id"/> (built here, callers don't need
    /// to set it). A play already reported by another source (same MatchKey, start within
    /// ListeningEvent.MatchTolerance) is merged into it instead of being added. Returns how many
    /// plays were new.
    /// </summary>
    Task<int> UpsertAsync(IReadOnlyCollection<ListeningEvent> events, string source, CancellationToken ct = default);
}
