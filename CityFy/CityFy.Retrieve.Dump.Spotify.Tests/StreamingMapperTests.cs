using CityFy.Retrieve.Dump.Spotify.Mappers;
using CityFy.Retrieve.Dump.Spotify.Models;

namespace CityFy.Retrieve.Dump.Spotify.Tests;

public sealed class StreamingMapperTests
{
    private static StreamingBl Entry(string? trackUri) => new()
    {
        Timestamp = new DateTime(2023, 5, 1, 10, 0, 0, DateTimeKind.Utc),
        MsPlayed = 185000,
        TrackName = "Song",
        ArtistName = "Artist",
        AlbumName = "Album",
        SpotifyTrackUri = trackUri,
        Skipped = true,
        IpAddress = "203.0.113.7",
        Platform = "android",
        Country = "IT"
    };

    [Fact]
    public void MusicPlay_IsMappedWithOwnerAndUpload()
    {
        var e = new StreamingMapper().MapToListeningEvent(Entry("spotify:track:t1"), "user1", "upload1");

        Assert.NotNull(e);
        Assert.Equal("user1", e.UserId);
        Assert.Equal("spotify:track:t1", e.TrackUri);
        Assert.Equal("Artist", e.ArtistName);
        Assert.Equal(185000, e.MsPlayed);
        Assert.True(e.Skipped);
        Assert.Equal("upload1", e.SourceRef);
        Assert.Equal(DateTimeKind.Utc, e.PlayedAt.Kind);
        Assert.Null(e.ArtistUri); // the export has no artist id
    }

    [Theory]
    [InlineData(null)]                      // podcast episode / audiobook
    [InlineData("spotify:episode:e1")]
    [InlineData("")]
    public void NonMusicEntries_AreSkipped(string? uri)
    {
        Assert.Null(new StreamingMapper().MapToListeningEvent(Entry(uri), "user1", "upload1"));
    }
}
