using System.Data;
using System.Diagnostics;
using System.Text.RegularExpressions;
using CityFy.Retrieve.MusicBrainz.Data;
using CityFy.Retrieve.MusicBrainz.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CityFy.Retrieve.MusicBrainz.Services;

/// <summary>
/// Reads the extracted dump tables by column position (MusicBrainz schema) and replaces the
/// content of the database in a single transaction.
/// </summary>
public sealed partial class MusicBrainzImporter(
    MusicBrainzContext db,
    ILogger<MusicBrainzImporter> logger,
    ImportProgress progress) : IMusicBrainzImporter
{
    /// <summary>Tables in reading order (the progress of the reading phase is by bytes over all of them).</summary>
    private static readonly string[] ReadOrder =
        ["genre", "link_type", "link", "l_genre_genre", "tag", "artist_tag", "artist", "l_artist_url", "url"];

    private readonly ImportProgress _progress = progress;

    [GeneratedRegex(@"^https?://open\.spotify\.com/artist/([A-Za-z0-9]{22})/?$", RegexOptions.IgnoreCase)]
    private static partial Regex SpotifyArtistUrl();

    public async Task<RetrieveResult> ImportAsync(string mbdumpDir, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var data = Read(mbdumpDir, ct);
        await WriteAsync(data, ct);

        return new RetrieveResult
        {
            Genres = data.Genres.Count,
            GenreRelations = data.GenreRelations.Count,
            Artists = data.Artists.Count,
            ArtistGenres = data.ArtistGenres.Count,
            SpotifyLinks = data.SpotifyLinks.Count,
            Duration = sw.Elapsed
        };
    }

    public MusicBrainzData Read(string mbdumpDir, CancellationToken ct)
    {
        string Table(string name)
        {
            var path = Path.Combine(mbdumpDir, name);
            if (!File.Exists(path)) throw new FileNotFoundException($"Dump table '{name}' not found", path);
            return path;
        }

        // Sizes up front: also fails fast if a table is missing, before reading GBs of the others
        var sizes = ReadOrder.ToDictionary(t => t, t => new FileInfo(Table(t)).Length);
        var totalBytes = Math.Max(1, sizes.Values.Sum());

        IEnumerable<string[]> Rows(string name)
        {
            var index = Array.IndexOf(ReadOrder, name) + 1;
            var before = ReadOrder.TakeWhile(t => t != name).Sum(t => sizes[t]);
            var label = $"{name} ({index}/{ReadOrder.Length})";
            _progress.Report("Reading tables", 100.0 * before / totalBytes, label);
            return DumpTableReader.ReadRows(Table(name), ct, position =>
                _progress.Report("Reading tables", 100.0 * (before + position) / totalBytes,
                    $"{label} · {100.0 * position / Math.Max(1, sizes[name]):0}%"));
        }

        var data = new MusicBrainzData();

        // genre: id | gid | name | comment | edits_pending | last_updated
        var genreIdByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in Rows("genre"))
        {
            var genre = new Genre { Id = int.Parse(r[0]), Gid = r[1], Name = DumpTableReader.Unescape(r[2]) };
            data.Genres.Add(genre);
            genreIdByName.TryAdd(genre.Name, genre.Id);
        }
        logger.LogInformation("Genres: {Count}", data.Genres.Count);

        // link_type: id | parent | child_order | gid | entity_type0 | entity_type1 | name | ...
        var genreLinkTypes = new Dictionary<int, string>();
        var artistUrlLinkTypes = new HashSet<int>();
        foreach (var r in Rows("link_type"))
        {
            var (e0, e1, name) = (r[4], r[5], r[6]);
            if (e0 == "genre" && e1 == "genre") genreLinkTypes[int.Parse(r[0])] = name;
            else if (e0 == "artist" && e1 == "url") artistUrlLinkTypes.Add(int.Parse(r[0]));
        }

        // link: id | link_type | ...  (only the link types we care about)
        var genreLinks = new Dictionary<int, string>();
        var artistUrlLinks = new HashSet<int>();
        foreach (var r in Rows("link"))
        {
            var linkType = int.Parse(r[1]);
            if (genreLinkTypes.TryGetValue(linkType, out var relType)) genreLinks[int.Parse(r[0])] = relType;
            else if (artistUrlLinkTypes.Contains(linkType)) artistUrlLinks.Add(int.Parse(r[0]));
        }

        // l_genre_genre: id | link | entity0 | entity1 | ...  ("entity0 {type} entity1")
        var genreIds = data.Genres.Select(g => g.Id).ToHashSet();
        var seenRelations = new HashSet<(int, int, string)>();
        foreach (var r in Rows("l_genre_genre"))
        {
            if (!genreLinks.TryGetValue(int.Parse(r[1]), out var relType)) continue;
            int from = int.Parse(r[2]), to = int.Parse(r[3]);
            if (!genreIds.Contains(from) || !genreIds.Contains(to)) continue;
            if (seenRelations.Add((from, to, relType)))
                data.GenreRelations.Add(new GenreRelation { FromGenreId = from, ToGenreId = to, RelationType = relType });
        }
        logger.LogInformation("Genre relations: {Count}", data.GenreRelations.Count);

        // tag (derived): id | name | ref_count  -> only tags that are official genres
        var genreByTag = new Dictionary<int, int>();
        foreach (var r in Rows("tag"))
        {
            if (genreIdByName.TryGetValue(DumpTableReader.Unescape(r[1]), out var genreId))
                genreByTag[int.Parse(r[0])] = genreId;
        }

        // artist_tag (derived): artist | tag | count | last_updated
        var votes = new Dictionary<(int Artist, int Genre), int>();
        foreach (var r in Rows("artist_tag"))
        {
            if (!genreByTag.TryGetValue(int.Parse(r[1]), out var genreId)) continue;
            var count = int.Parse(r[2]);
            if (count <= 0) continue; // net-downvoted
            var key = (int.Parse(r[0]), genreId);
            votes[key] = votes.GetValueOrDefault(key) + count;
        }
        foreach (var ((artistId, genreId), count) in votes)
            data.ArtistGenres.Add(new ArtistGenre { ArtistId = artistId, GenreId = genreId, Votes = count });

        var votesByArtist = data.ArtistGenres.GroupBy(ag => ag.ArtistId).ToDictionary(g => g.Key, g => g.Sum(x => x.Votes));
        logger.LogInformation("Artist genres: {Count} for {Artists} artists", data.ArtistGenres.Count, votesByArtist.Count);

        // artist: id | gid | name | sort_name | ...  (only artists with at least one genre)
        foreach (var r in Rows("artist"))
        {
            var id = int.Parse(r[0]);
            if (!votesByArtist.TryGetValue(id, out var total)) continue;
            data.Artists.Add(new Artist { Id = id, Gid = r[1], Name = DumpTableReader.Unescape(r[2]), TotalGenreVotes = total });
        }

        // Every ArtistGenre must point to an imported artist (FK)
        var artistIds = data.Artists.Select(a => a.Id).ToHashSet();
        data.ArtistGenres.RemoveAll(ag => !artistIds.Contains(ag.ArtistId));
        logger.LogInformation("Artists: {Count}", data.Artists.Count);

        // l_artist_url: id | link | entity0 (artist) | entity1 (url) | ...
        var artistsByUrl = new Dictionary<int, List<int>>();
        foreach (var r in Rows("l_artist_url"))
        {
            if (!artistUrlLinks.Contains(int.Parse(r[1]))) continue;
            var artistId = int.Parse(r[2]);
            if (!artistIds.Contains(artistId)) continue;
            var urlId = int.Parse(r[3]);
            if (!artistsByUrl.TryGetValue(urlId, out var list)) artistsByUrl[urlId] = list = new List<int>(1);
            list.Add(artistId);
        }

        // url: id | gid | url | edits_pending | last_updated  (several GB: streamed, filtered by id)
        var seenLinks = new HashSet<(int, string)>();
        foreach (var r in Rows("url"))
        {
            if (!artistsByUrl.TryGetValue(int.Parse(r[0]), out var artists)) continue;
            var m = SpotifyArtistUrl().Match(r[2]);
            if (!m.Success) continue;
            foreach (var artistId in artists)
                if (seenLinks.Add((artistId, m.Groups[1].Value)))
                    data.SpotifyLinks.Add(new ArtistSpotifyLink { ArtistId = artistId, SpotifyArtistId = m.Groups[1].Value });
        }
        logger.LogInformation("Spotify links: {Count}", data.SpotifyLinks.Count);

        return data;
    }

    /// <summary>Replaces all MusicBrainz data atomically: readers never see a half-imported state.</summary>
    private async Task WriteAsync(MusicBrainzData data, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Progress by rows over the five tables (a retry of the strategy starts again from 0)
            _writtenRows = 0;
            _totalRows = Math.Max(1, data.Genres.Count + data.GenreRelations.Count + data.Artists.Count
                + data.ArtistGenres.Count + data.SpotifyLinks.Count);
            _progress.Report("Writing database", 0, "Clearing the previous tables");

            // Children first (FKs)
            await db.ArtistSpotifyLinks.ExecuteDeleteAsync(ct);
            await db.ArtistGenres.ExecuteDeleteAsync(ct);
            await db.GenreRelations.ExecuteDeleteAsync(ct);
            await db.Artists.ExecuteDeleteAsync(ct);
            await db.Genres.ExecuteDeleteAsync(ct);

            var conn = (SqlConnection)db.Database.GetDbConnection();
            var sqlTx = (SqlTransaction)tx.GetDbTransaction();

            await BulkCopyAsync(ct, conn, sqlTx, "Genres", data.Genres,
                ("Id", typeof(int), g => g.Id), ("Gid", typeof(string), g => g.Gid), ("Name", typeof(string), g => g.Name));
            await BulkCopyAsync(ct, conn, sqlTx, "GenreRelations", data.GenreRelations,
                ("FromGenreId", typeof(int), r => r.FromGenreId), ("ToGenreId", typeof(int), r => r.ToGenreId),
                ("RelationType", typeof(string), r => r.RelationType));
            await BulkCopyAsync(ct, conn, sqlTx, "Artists", data.Artists,
                ("Id", typeof(int), a => a.Id), ("Gid", typeof(string), a => a.Gid), ("Name", typeof(string), a => a.Name),
                ("TotalGenreVotes", typeof(int), a => a.TotalGenreVotes));
            await BulkCopyAsync(ct, conn, sqlTx, "ArtistGenres", data.ArtistGenres,
                ("ArtistId", typeof(int), ag => ag.ArtistId), ("GenreId", typeof(int), ag => ag.GenreId), ("Votes", typeof(int), ag => ag.Votes));
            await BulkCopyAsync(ct, conn, sqlTx, "ArtistSpotifyLinks", data.SpotifyLinks,
                ("ArtistId", typeof(int), l => l.ArtistId), ("SpotifyArtistId", typeof(string), l => l.SpotifyArtistId));

            await tx.CommitAsync(ct);
        });
    }

    private async Task BulkCopyAsync<T>(CancellationToken ct, SqlConnection conn, SqlTransaction tx, string table, IReadOnlyCollection<T> rows,
        params (string Column, Type Type, Func<T, object> Value)[] columns)
    {
        using var dt = new DataTable();
        foreach (var c in columns) dt.Columns.Add(c.Column, c.Type);
        foreach (var row in rows)
        {
            var values = new object[columns.Length];
            for (var i = 0; i < columns.Length; i++) values[i] = columns[i].Value(row);
            dt.Rows.Add(values);
        }

        using var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.CheckConstraints, tx)
        {
            DestinationTableName = $"[dbo].[{table}]",
            BulkCopyTimeout = 0,
            BatchSize = 50_000,
            NotifyAfter = 10_000
        };
        foreach (var c in columns) bulk.ColumnMappings.Add(c.Column, c.Column);

        var before = _writtenRows;
        void Report(long copied) => _progress.Report("Writing database", 100.0 * (before + copied) / _totalRows,
            $"{table} · {copied:N0} / {rows.Count:N0} rows");
        bulk.SqlRowsCopied += (_, e) => Report(e.RowsCopied);
        Report(0);

        // Cancellable: the transaction is rolled back and the previous catalog stays in place
        await bulk.WriteToServerAsync(dt, ct);
        _writtenRows += rows.Count;
        logger.LogInformation("Bulk copied {Count} rows into {Table}", rows.Count, table);
    }

    private long _writtenRows;
    private long _totalRows = 1;
}
