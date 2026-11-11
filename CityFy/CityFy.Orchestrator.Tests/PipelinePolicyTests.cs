using CityFy.Orchestrator.Services;

namespace CityFy.Orchestrator.Tests;

public class PipelinePolicyTests
{
    private static readonly DateTime Created = new(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(5);

    [Fact]
    public void FirstImport_StartsAfterTheDebounce()
    {
        Assert.Equal(Created.AddSeconds(30), PipelinePolicy.ComputeDueAt(Created, Created, Debounce, MaxWait));
    }

    [Fact]
    public void NewImport_PostponesTheStart()
    {
        var now = Created.AddSeconds(20);

        Assert.Equal(now.AddSeconds(30), PipelinePolicy.ComputeDueAt(Created, now, Debounce, MaxWait));
    }

    [Fact]
    public void ContinuousImports_CannotPostponeBeyondMaxWait()
    {
        var now = Created.AddMinutes(4).AddSeconds(50);

        Assert.Equal(Created.AddMinutes(5), PipelinePolicy.ComputeDueAt(Created, now, Debounce, MaxWait));
    }

    [Fact]
    public void ZeroDebounce_StartsImmediately()
    {
        Assert.Equal(Created, PipelinePolicy.ComputeDueAt(Created, Created, TimeSpan.Zero, TimeSpan.Zero));
    }
}
