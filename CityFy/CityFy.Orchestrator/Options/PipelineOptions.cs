using System.ComponentModel.DataAnnotations;

namespace CityFy.Orchestrator.Options;

/// <summary>"Pipeline" configuration section.</summary>
public sealed class PipelineOptions
{
    public const string SectionName = "Pipeline";

    /// <summary>Quiet time after the last import before the catalog is refreshed.</summary>
    [Range(0, 3600)]
    public int DebounceSeconds { get; set; } = 30;

    /// <summary>Upper bound of the debounce: a continuous stream of imports cannot postpone the build forever.</summary>
    [Range(0, 86400)]
    public int MaxWaitSeconds { get; set; } = 300;

    /// <summary>A Building run without answer after this is marked Failed.</summary>
    [Range(1, 1440)]
    public int BuildTimeoutMinutes { get; set; } = 30;

    [Range(1, 300)]
    public int PollSeconds { get; set; } = 5;

}
