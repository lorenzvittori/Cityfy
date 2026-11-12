using ServiceDefault.Listening;

namespace ServiceDefault.Tests;

public sealed class ListeningEventTests
{
    [Fact]
    public void BuildId_TruncatesToTheSecond_SoDumpAndApiPlaysCollapse()
    {
        // Export 'ts' has second precision, API 'played_at' millisecond precision
        var fromDump = ListeningEvent.BuildId("u", "spotify:track:t", new DateTime(2026, 11, 10, 21, 15, 30, DateTimeKind.Utc));
        var fromApi = ListeningEvent.BuildId("u", "spotify:track:t", new DateTime(2026, 11, 10, 21, 15, 30, 987, DateTimeKind.Utc));

        Assert.Equal(fromDump, fromApi);
        Assert.Equal("u:spotify:track:t:2026-11-10T21:15:30Z", fromDump);
    }

    [Fact]
    public void BuildId_DiffersPerUserAndTrack()
    {
        var at = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.NotEqual(ListeningEvent.BuildId("u1", "spotify:track:t", at), ListeningEvent.BuildId("u2", "spotify:track:t", at));
        Assert.NotEqual(ListeningEvent.BuildId("u1", "spotify:track:a", at), ListeningEvent.BuildId("u1", "spotify:track:b", at));
    }
}
