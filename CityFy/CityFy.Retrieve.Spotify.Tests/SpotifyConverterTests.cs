using CityFy.Retrieve.Spotify.Mappers;
using CityFy.Retrieve.Spotify.Models;

namespace CityFy.Retrieve.Spotify.Tests;

public sealed class SpotifyConverterTests
{
    private static SpotifyPlayHistoryDto Play(string? uri, params (string Name, string Uri)[] artists) => new()
    {
        PlayedAt = new DateTime(2026, 11, 10, 21, 0, 0, DateTimeKind.Utc),
        Track = uri == null ? null : new SpotifyTrackDto
        {
            Uri = uri,
            Name = "Song",
            Album = new SpotifyAlbumDto { Name = "Album" },
            Artists = artists.Select(a => new SpotifyArtistDto { Name = a.Name, Uri = a.Uri }).ToList()
        },
        Context = new SpotifyContextDto { Uri = "spotify:playlist:p1" }
    };

    [Fact]
    public void Maps_PrimaryArtist_Context_AndSource()
    {
        var events = new SpotifyConverter().ToListeningEvents(
            [Play("spotify:track:t1", ("Main", "spotify:artist:a1"), ("Feat", "spotify:artist:a2"))], "user1", "job1");

        var e = Assert.Single(events);
        Assert.Equal("user1", e.UserId);
        Assert.Equal("spotify:track:t1", e.TrackUri);
        Assert.Equal("Main", e.ArtistName);
        Assert.Equal("spotify:artist:a1", e.ArtistUri);
        Assert.Equal("Album", e.AlbumName);
        Assert.Equal("spotify:playlist:p1", e.ContextUri);
        Assert.Equal("job1", e.SourceRef);
        Assert.Equal(DateTimeKind.Utc, e.PlayedAt.Kind);
        Assert.Null(e.MsPlayed); // unknown from the API
    }

    [Fact]
    public void Skips_LocalFiles_Episodes_AndMissingTracks()
    {
        var events = new SpotifyConverter().ToListeningEvents(
            [Play("spotify:local:x"), Play("spotify:episode:e1"), Play(null)], "user1", "job1");

        Assert.Empty(events);
    }
}
