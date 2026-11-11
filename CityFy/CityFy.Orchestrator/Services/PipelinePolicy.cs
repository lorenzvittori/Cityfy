namespace CityFy.Orchestrator.Services;

public static class PipelinePolicy
{
    /// <summary>
    /// Debounce: every new import pushes the start to now + debounce, but never beyond
    /// createdAt + maxWait.
    /// </summary>
    public static DateTime ComputeDueAt(DateTime createdAt, DateTime now, TimeSpan debounce, TimeSpan maxWait)
    {
        var due = now + debounce;
        var limit = createdAt + maxWait;
        return due < limit ? due : limit;
    }
}
