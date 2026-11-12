namespace CityFy.Orchestrator.SubServices;

/// <summary>When a Pending run starts (pure, Pipeline options).</summary>
public interface IPipelineDebounceSubService
{
    /// <summary>
    /// Debounce: every new import pushes the start to now + debounce, but never beyond
    /// createdAt + maxWait.
    /// </summary>
    DateTime ComputeDueAt(DateTime createdAt, DateTime now, TimeSpan debounce, TimeSpan maxWait);

    /// <summary><see cref="ComputeDueAt(DateTime, DateTime, TimeSpan, TimeSpan)"/> with the configured debounce; now when <paramref name="immediate"/>.</summary>
    DateTime DueAt(DateTime createdAt, DateTime now, bool immediate);

    /// <summary>Next attempt of a run whose command could not be published.</summary>
    DateTime RetryAt(DateTime now);
}
