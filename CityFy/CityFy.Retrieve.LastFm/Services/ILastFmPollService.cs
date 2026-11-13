namespace CityFy.Retrieve.LastFm.Services;

/// <summary>Reading of the new scrobbles and of the track playing, account by account at its own cadence.</summary>
public interface ILastFmPollService
{
    /// <summary>
    /// Reads the accounts due now (at most PollBatchSize). An account that fails is rescheduled (or
    /// suspended when the account itself cannot be read) and does not stop the others. Returns how many were read.
    /// </summary>
    Task<int> PollDueAsync(CancellationToken ct);
}
