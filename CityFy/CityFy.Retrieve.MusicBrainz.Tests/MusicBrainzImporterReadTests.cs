using CityFy.Retrieve.MusicBrainz.Models;
using CityFy.Retrieve.MusicBrainz.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CityFy.Retrieve.MusicBrainz.Tests;

/// <summary>
/// Reads a tiny dump whose rows follow the real MusicBrainz column layouts
/// (checked against the 20260923 full export).
/// </summary>
public sealed class MusicBrainzImporterReadTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mbdump-test-").FullName;

    public MusicBrainzImporterReadTests()
    {
        // genre: id | gid | name | comment | edits_pending | last_updated
        Write("genre",
            "1\t54c01942-22fd-4184-9877-1db0089b18f1\trock\t\t0\t2019-05-13 17:46:28+00",
            "2\t7dc2b20f-3953-4874-b9bf-41b8ba06d20c\tgrunge\t\t0\t2019-05-13 17:46:28+00",
            "3\t11111111-2222-3333-4444-555555555555\tdrum and bass\t\t0\t2019-05-13 17:46:28+00");

        // link_type: id | parent | child_order | gid | entity_type0 | entity_type1 | name | ...
        Write("link_type",
            "1095\t\\N\t0\tgid-a\tgenre\tgenre\tsubgenre\t\tsubgenres\tsubgenre of\thas subgenre\t2022-01-01\tf\tf\t0\t0",
            "1097\t\\N\t0\tgid-b\tgenre\tgenre\tinfluenced by\t\tinfluenced by\tinfluenced\tinfluenced by\t2022-01-01\tf\tf\t0\t0",
            "194\t\\N\t0\tgid-c\tartist\turl\tfree streaming\t\tstream for free\tfree streaming page for\tfree streaming\t2022-01-01\tf\tf\t0\t0",
            "12\t\\N\t0\tgid-d\trelease_group\trelease_group\tcovers and versions\t\tcovers\tcovered\tcovers\t2014-05-18\tf\tf\t0\t0");

        // link: id | link_type | ...
        Write("link",
            "500\t1095\t\\N\t\\N\t\\N\t\\N\t\\N\t\\N\t0\t2022-01-01\tf",
            "501\t1097\t\\N\t\\N\t\\N\t\\N\t\\N\t\\N\t0\t2022-01-01\tf",
            "502\t194\t\\N\t\\N\t\\N\t\\N\t\\N\t\\N\t0\t2022-01-01\tf",
            "503\t12\t\\N\t\\N\t\\N\t\\N\t\\N\t\\N\t0\t2022-01-01\tf");

        // l_genre_genre: id | link | entity0 | entity1 | ...  ("rock has subgenre grunge")
        Write("l_genre_genre",
            "1\t500\t1\t2\t0\t2022-09-16\t0\t\t",
            "2\t501\t3\t1\t0\t2022-09-16\t0\t\t",
            "3\t500\t1\t2\t0\t2022-09-16\t0\t\t"); // duplicate

        // tag (derived): id | name | ref_count
        Write("tag",
            "10\trock\t100",
            "11\tGrunge\t50",          // case differs from the genre name
            "12\tseen live\t999");     // not a genre

        // artist_tag (derived): artist | tag | count | last_updated
        Write("artist_tag",
            "100\t10\t3\t2020-01-01",
            "100\t11\t5\t2020-01-01",
            "100\t12\t9\t2020-01-01",
            "200\t10\t-2\t2020-01-01",  // net down-voted
            "300\t12\t4\t2020-01-01");  // only non-genre tags

        // artist: id | gid | name | sort_name | ...
        Write("artist",
            "100\taaaaaaaa-0000-0000-0000-000000000100\tNirvana\tNirvana\t\\N",
            "200\taaaaaaaa-0000-0000-0000-000000000200\tDownvoted\tDownvoted\t\\N",
            "300\taaaaaaaa-0000-0000-0000-000000000300\tNo Genres\tNo Genres\t\\N",
            "400\taaaaaaaa-0000-0000-0000-000000000400\tTab\\tName\tTab Name\t\\N");

        // l_artist_url: id | link | entity0 (artist) | entity1 (url) | ...
        Write("l_artist_url",
            "1\t502\t100\t9000\t0\t2020-01-01\t0\t\t",
            "2\t502\t100\t9001\t0\t2020-01-01\t0\t\t",
            "3\t502\t300\t9002\t0\t2020-01-01\t0\t\t", // artist not imported
            "4\t502\t100\t9003\t0\t2020-01-01\t0\t\t"); // same id as 9000 but lowercase

        // url: id | gid | url | edits_pending | last_updated
        Write("url",
            "9000\tgid\thttps://open.spotify.com/artist/6olE6TJLqED3rqDCT0FyPh\t0\t2020-01-01",
            "9001\tgid\thttps://www.youtube.com/channel/abc\t0\t2020-01-01",
            "9002\tgid\thttps://open.spotify.com/artist/0000000000000000000000\t0\t2020-01-01",
            "9003\tgid\thttps://open.spotify.com/artist/6ole6tjlqed3rqdct0fyph\t0\t2020-01-01");
    }

    private void Write(string table, params string[] rows) =>
        File.WriteAllLines(Path.Combine(_dir, table), rows);

    private MusicBrainzData Read() =>
        new MusicBrainzImporter(null!, NullLogger<MusicBrainzImporter>.Instance, new ImportProgress()).Read(_dir, CancellationToken.None);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Genres_UseTheNameColumn_NotTheGid()
    {
        var data = Read();

        Assert.Equal(["rock", "grunge", "drum and bass"], data.Genres.Select(g => g.Name));
        Assert.Equal("54c01942-22fd-4184-9877-1db0089b18f1", data.Genres[0].Gid);
    }

    [Fact]
    public void GenreRelations_KeepDirectionAndType_WithoutDuplicates()
    {
        var data = Read();

        Assert.Equal(2, data.GenreRelations.Count);
        Assert.Contains(data.GenreRelations, r => r is { FromGenreId: 1, ToGenreId: 2, RelationType: GenreRelationTypes.Subgenre });
        Assert.Contains(data.GenreRelations, r => r is { FromGenreId: 3, ToGenreId: 1, RelationType: GenreRelationTypes.InfluencedBy });
    }

    [Fact]
    public void Artists_AreImportedOnlyWithPositiveGenreVotes()
    {
        var data = Read();

        var artist = Assert.Single(data.Artists);
        Assert.Equal("Nirvana", artist.Name);
        Assert.Equal(8, artist.TotalGenreVotes);
        Assert.Equal(2, data.ArtistGenres.Count);
        Assert.Contains(data.ArtistGenres, ag => ag is { ArtistId: 100, GenreId: 2, Votes: 5 });
    }

    [Fact]
    public void SpotifyLinks_AreExtractedFromSpotifyArtistUrlsOnly_KeepingCase()
    {
        var data = Read();

        // Spotify ids are case-sensitive: the lowercase variant is a different id (stored as such,
        // the SQL column has a binary collation so they do not collide on the primary key)
        Assert.Equal(2, data.SpotifyLinks.Count);
        Assert.All(data.SpotifyLinks, l => Assert.Equal(100, l.ArtistId));
        Assert.Contains(data.SpotifyLinks, l => l.SpotifyArtistId == "6olE6TJLqED3rqDCT0FyPh");
        Assert.Contains(data.SpotifyLinks, l => l.SpotifyArtistId == "6ole6tjlqed3rqdct0fyph");
    }

    [Fact]
    public void Read_ReportsProgressTableByTable()
    {
        var progress = new ImportProgress();

        new MusicBrainzImporter(null!, NullLogger<MusicBrainzImporter>.Instance, progress).Read(_dir, CancellationToken.None);

        Assert.Equal("Reading tables", progress.Current!.Phase);
        Assert.StartsWith("url (9/9)", progress.Current.Detail);
        Assert.InRange(progress.Current.Percent!.Value, 0, 100);
    }

    [Fact]
    public void MissingTable_FailsWithTheTableName()
    {
        File.Delete(Path.Combine(_dir, "artist_tag"));

        var ex = Assert.Throws<FileNotFoundException>(() => Read());
        Assert.Contains("artist_tag", ex.Message);
    }
}
