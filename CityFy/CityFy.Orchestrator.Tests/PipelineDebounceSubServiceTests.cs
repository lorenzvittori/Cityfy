using CityFy.Orchestrator.Options;
using CityFy.Orchestrator.SubServices;

namespace CityFy.Orchestrator.Tests;

public sealed class PipelineDebounceSubServiceTests
{
    private static readonly DateTime Created = new(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(30);
    private static readonly PipelineDebounceSubService Debounce = new(Microsoft.Extensions.Options.Options.Create(new PipelineOptions()));
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(5);

    [Fact]
    public void FirstImport_StartsAfterTheDebounce()
    {
        Assert.Equal(Created.AddSeconds(30), Debounce.ComputeDueAt(Created, Created, DebounceDelay, MaxWait));
    }

    [Fact]
    public void NewImport_PostponesTheStart()
    {
        var now = Created.AddSeconds(20);

        Assert.Equal(now.AddSeconds(30), Debounce.ComputeDueAt(Created, now, DebounceDelay, MaxWait));
    }

    [Fact]
    public void ContinuousImports_CannotPostponeBeyondMaxWait()
    {
        var now = Created.AddMinutes(4).AddSeconds(50);

        Assert.Equal(Created.AddMinutes(5), Debounce.ComputeDueAt(Created, now, DebounceDelay, MaxWait));
    }

    [Fact]
    public void ZeroDebounce_StartsImmediately()
    {
        Assert.Equal(Created, Debounce.ComputeDueAt(Created, Created, TimeSpan.Zero, TimeSpan.Zero));
    }
}
