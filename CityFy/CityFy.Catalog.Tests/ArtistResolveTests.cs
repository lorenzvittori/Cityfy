using CityFy.Catalog.Clients;
using CityFy.Catalog.Models;
using CityFy.Catalog.SubServices;

namespace CityFy.Catalog.Tests;

/// <summary>Which key of the plays finds the MusicBrainz artist: Spotify id, then MBID (Last.fm), then name.</summary>
public sealed class ArtistResolveTests
{
    private sealed class FakeMusicBrainz : IMusicBrainzClient
    {
        public ResolveArtistsRequest? Request { get; private set; }

        public Task<ResolveArtistsResponse> ResolveArtistsAsync(ResolveArtistsRequest request, CancellationToken ct)
        {
            Request = request;
            return Task.FromResult(new ResolveArtistsResponse(
                [Artist("sp1", "spotify-url", 1)],
                [Artist("Genesis", "name", 2), Artist("Unknown", "none", null)],
                [Artist("mb-genesis-uk", "mbid", 3)]));
        }

        public Task<GenreGraph> GetGenreGraphAsync(CancellationToken ct) => throw new NotSupportedException();

        private static ResolvedArtist Artist(string input, string match, int? id) =>
            new(input, match, id, id is null ? null : $"gid-{id}", id is null ? null : input, []);
    }

    private static ArtistPlays Plays(string name, string? uri = null, string? mbid = null) =>
        new() { ArtistName = name, ArtistUri = uri, ArtistMbid = mbid, Plays = 1, QualifiedPlays = 1 };

    [Fact]
    public async Task TheMostPreciseKeyWins()
    {
        var musicBrainz = new FakeMusicBrainz();
        var resolve = new ArtistResolveSubService(musicBrainz);

        var resolved = await resolve.ResolveAsync(
        [
            Plays("Radiohead", uri: "spotify:artist:sp1", mbid: "mb-other"), // Spotify id first
            Plays("Genesis", mbid: "mb-genesis-uk"),                          // MBID before the (homonym) name
            Plays("Unknown")
        ], CancellationToken.None);

        Assert.Equal(1, resolved["Radiohead"].ArtistId);
        Assert.Equal(3, resolved["Genesis"].ArtistId);
        Assert.False(resolved.ContainsKey("Unknown"));
        Assert.Equal(["mb-other", "mb-genesis-uk"], musicBrainz.Request!.Mbids);
    }

    [Fact]
    public async Task MbidNotInMusicBrainz_FallsBackToTheName()
    {
        var resolve = new ArtistResolveSubService(new FakeMusicBrainz());

        var resolved = await resolve.ResolveAsync([Plays("Genesis", mbid: "mb-missing")], CancellationToken.None);

        Assert.Equal(2, resolved["Genesis"].ArtistId);
    }
}
