using CityFy.Orchestrator.Options;
using Microsoft.Extensions.Options;

namespace CityFy.Orchestrator.SubServices;

public sealed class PipelineDebounceSubService(IOptions<PipelineOptions> options) : IPipelineDebounceSubService
{
    /// <summary>Lower bound of the retry delay, even with a zero debounce.</summary>
    private const int MinRetryDelaySeconds = 10;

    private readonly PipelineOptions _options = options.Value;

    public DateTime ComputeDueAt(DateTime createdAt, DateTime now, TimeSpan debounce, TimeSpan maxWait)
    {
        var due = now + debounce;
        var limit = createdAt + maxWait;
        return due < limit ? due : limit;
    }

    public DateTime DueAt(DateTime createdAt, DateTime now, bool immediate) =>
        immediate
            ? now
            : ComputeDueAt(createdAt, now, TimeSpan.FromSeconds(_options.DebounceSeconds), TimeSpan.FromSeconds(_options.MaxWaitSeconds));

    public DateTime RetryAt(DateTime now) => now.AddSeconds(Math.Max(_options.DebounceSeconds, MinRetryDelaySeconds));
}
