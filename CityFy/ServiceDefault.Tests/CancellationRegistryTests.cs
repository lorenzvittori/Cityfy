using ServiceDefault.Hosting;

namespace ServiceDefault.Tests;

public sealed class CancellationRegistryTests
{
    [Fact]
    public void Cancel_TripsTheTokenOfTheRunningProcess()
    {
        var registry = new CancellationRegistry();
        var cts = registry.Register("job1", CancellationToken.None);

        Assert.True(registry.Cancel("job1"));
        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public void Cancel_OfAnUnknownOrFinishedProcess_ReturnsFalse()
    {
        var registry = new CancellationRegistry();
        var cts = registry.Register("job1", CancellationToken.None);
        registry.Unregister("job1", cts);

        Assert.False(registry.Cancel("job1"));
        Assert.False(registry.Cancel("never-started"));
    }

    [Fact]
    public void Shutdown_AlsoCancelsTheProcess()
    {
        using var shutdown = new CancellationTokenSource();
        var registry = new CancellationRegistry();
        var cts = registry.Register("job1", shutdown.Token);

        shutdown.Cancel();

        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public void Unregister_DoesNotRemoveANewerRunWithTheSameId()
    {
        var registry = new CancellationRegistry();
        var first = registry.Register("job1", CancellationToken.None);
        var second = registry.Register("job1", CancellationToken.None); // re-run replaced the entry

        registry.Unregister("job1", first);

        Assert.True(registry.Cancel("job1"));
        Assert.True(second.IsCancellationRequested);
    }
}
